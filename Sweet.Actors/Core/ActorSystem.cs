#region License
//  The MIT License (MIT)
//
//  Copyright (c) 2017, Cagatay Dogan
//
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
//
//      The above copyright notice and this permission notice shall be included in
//      all copies or substantial portions of the Software.
//
//      THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//      IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//      FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//      AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//      LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//      OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//      THE SOFTWARE.
#endregion License

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public sealed class ActorSystem : Disposable, IResponseHandler
    {
        private enum ProcessType
        {
            Class,
            Function,
            Remote
        }

        private class ProcessRegistery : IDisposable
        {            
            public ProcessRegistery(Process process, Type actorType, 
                ProcessType processType = ProcessType.Class,
                RemoteAddress remoteAddress = null)
            {
                Process = process;
                ActorType = actorType;
                ProcessType = processType;
                RemoteAddress = remoteAddress;
            }
            
            public void Dispose()
            {
                using (var process = Process)
                    Process = null;
            }

            public Type ActorType { get; }

            public Process Process { get; private set; }

            public ProcessType ProcessType { get; }

            public RemoteAddress RemoteAddress { get; }
        }

        private class AnonymousNameGenerator : Id<ActorSystem>
        {
            public AnonymousNameGenerator(long major, long majorRevision, long minor, long minorRevision)
                : base(major, majorRevision, minor, minorRevision)
            { }

            public static string Next()
            {
                var buffer = Generate();
                return $"[{Common.ProcessId}-{buffer[0]}.{buffer[1]}.{buffer[2]}.{buffer[3]}]";
            }
        }

        private static readonly ConcurrentDictionary<string, ActorSystem> _systemRegistery =
            new ConcurrentDictionary<string, ActorSystem>();

        private IRemoteManager _remoteManager;

        private ConcurrentDictionary<string, ProcessRegistery> _processRegistery =
            new ConcurrentDictionary<string, ProcessRegistery>();

        private ActorSystem(ActorSystemOptions options)
        {
            Options = options ?? ActorSystemOptions.Default;
            Name = options.Name;
        }

        public string Name { get; }

        public ActorSystemOptions Options { get; }

        internal IRemoteManager RemoteManager => _remoteManager;

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                _remoteManager?.Unbind(this);
                _systemRegistery.TryRemove(Name, out ActorSystem actorSystem);

                var processes = Interlocked.Exchange(ref _processRegistery, null);
                foreach (var registery in processes.Values)
                {
                    try
                    {
                        registery.Dispose();
                    }
                    catch (Exception)
                    { }
                }
            }
            base.OnDispose(disposing);
        }

        internal void SetRemoteManager(IRemoteManager remoteManager)
        {
            _remoteManager = remoteManager;
        }

        public static bool TryGet(string actorSystemName, out ActorSystem actorSystem)
        {
            actorSystem = null;
            return _systemRegistery.TryGetValue(actorSystemName, out actorSystem);
        }

        public static bool TryGetLocal(Aid localAid, out Pid pid)
        {
            pid = null;
            if (localAid != null)
            {
                pid = localAid as Pid;
                if (pid?.Process != null)
                    return true;

                if (TryGet(localAid.ActorSystem, out ActorSystem actorSystem))
                    return actorSystem.TryGet(localAid.Actor, out pid);
            }
            return false;
        }

        public bool TryGetRemote(Aid remoteAid, out Pid pid)
        {
            pid = null;
            if (remoteAid != null)
            {
                pid = remoteAid as Pid;
                if (pid?.Process != null)
                    return true;

                return TryGet($"{remoteAid.ActorSystem}/{remoteAid.Actor}", out pid);
            }
            return false;
        }

        public static ActorSystem GetOrAdd(ActorSystemOptions options = null)
        {
            options = (options ?? ActorSystemOptions.Default);

            var actorSystemName = options.Name?.Trim();
            if (String.IsNullOrEmpty(actorSystemName))
                throw new ArgumentNullException(nameof(options.Name));

            return _systemRegistery.GetOrAdd(options.Name,
                (sn) => new ActorSystem(options));
        }

        public bool TryGet(string actorName, out Pid pid)
        {
            pid = null;
            actorName = actorName?.Trim();

            if (!String.IsNullOrEmpty(actorName) &&
                _processRegistery.TryGetValue(actorName, out ProcessRegistery registery))
            {
                pid = registery.Process?.Pid;
                return pid != null;
            }
            return false;
        }

        public Pid From(ActorOptions options)
        {
            ThrowIfDisposed();

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (IsRemote(options))
                return FromRemote(options);

            Type actorType = null;

            var actor = options.Actor;
            if (actor == null)
                actorType = options.ActorType;

            if (actor == null && actorType == null)
                throw new ArgumentNullException(nameof(options.Actor));

            return GetOrAdd(options, actor, actorType);
        }

        private bool IsRemote(ActorOptions options)
        {
            return (options != null) && (options.EndPoint != null) &&
                !String.IsNullOrEmpty(options.RemoteActorSystem?.Trim());
        }

        public Pid FromType<T>(ActorOptions options = null)
            where T : class, IActor, new()
        {
            ThrowIfDisposed();
            return GetOrAdd(options, null, typeof(T));
        }

        public Pid FromActor(IActor actor, ActorOptions options = null)
        {
            ThrowIfDisposed();

            if (actor == null)
                throw new ArgumentNullException(nameof(actor));

            return GetOrAdd(options, actor, actor.GetType());
        }

        private Pid GetOrAdd(ActorOptions options, IActor actor, Type actorType)
        {
            options = (options ?? ActorOptions.Default);

            if (actor != null)
                actorType = actor.GetType();

            var actorName = options.Name?.Trim();
            if (String.IsNullOrEmpty(actorName))
                actorName = actorType.ToString();

            var isNew = false;
            var registery = _processRegistery.GetOrAdd(actorName,
                (an) =>
                {
                    isNew = true;

                    var sequentialInvokeLimit = options.SequentialInvokeLimit;
                    if (sequentialInvokeLimit < 1)
                        sequentialInvokeLimit = Options.SequentialInvokeLimit;

                    var p = new Process(an, this,
                                    actor ?? (IActor)Activator.CreateInstance(actorType),
                                    options.ErrorHandler ?? Options.ErrorHandler,
                                    GetSequentialInvokeLimit(options), options.InitialContextData);
                    return new ProcessRegistery(p, actorType);
                });

            if (!isNew && 
                ((registery.ProcessType != ProcessType.Class) || (registery.ActorType != actorType)))
                throw new Exception(Errors.ActorWithNameOfDifferentTypeAlreadyExists);

            return registery.Process.Pid;
        }

        private int GetSequentialInvokeLimit(ActorOptions options)
        {
            var result = options.SequentialInvokeLimit;
            if (result < 1)
                result = Options.SequentialInvokeLimit;

            return result;
        }
   
        public Pid FromFunction(Func<IContext, IMessage, Task> receiveFunc, ActorOptions options = null)
        {
            ThrowIfDisposed();

            if (receiveFunc == null)
                throw new ArgumentNullException(nameof(receiveFunc));

            options = (options ?? ActorOptions.Default);

            var actorName = options.Name?.Trim();
            if (String.IsNullOrEmpty(actorName))
                actorName = AnonymousNameGenerator.Next();

            var isNew = false;
            var registery = _processRegistery.GetOrAdd(actorName,
                (an) =>
                {
                    isNew = true;

                    var sequentialInvokeLimit = options.SequentialInvokeLimit;
                    if (sequentialInvokeLimit < 1)
                        sequentialInvokeLimit = Options.SequentialInvokeLimit;

                    var p = new FunctionCallProcess(actorName, this, receiveFunc, 
                                options.ErrorHandler ?? Options.ErrorHandler,
                                GetSequentialInvokeLimit(options), options.InitialContextData);

                    return new ProcessRegistery(p, typeof(FunctionCallProcess), ProcessType.Function);
                });

            if (!isNew)
            {
                if (registery.ProcessType != ProcessType.Function)
                    throw new Exception(Errors.ActorWithNameOfDifferentTypeAlreadyExists);

                var fp = registery.Process as FunctionCallProcess;
                if (fp == null)
                    throw new Exception(Errors.ActorWithNameOfDifferentTypeAlreadyExists);

                if (fp.ReceiveFunc != receiveFunc)
                    throw new Exception(Errors.ActorAlreadyExsists);
            }

            return registery.Process.Pid;
        }

        public Pid FromRemote(ActorOptions options)
        {
            ThrowIfDisposed();

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var remoteActorSystem = options.RemoteActorSystem?.Trim();
            if (String.IsNullOrEmpty(remoteActorSystem))
                throw new ArgumentNullException(nameof(options.RemoteActorSystem));

            var remoteActor = options.Name?.Trim();
            if (String.IsNullOrEmpty(remoteActor))
                throw new ArgumentNullException(nameof(options.Name));

            var isNew = false;
            var registery = _processRegistery.GetOrAdd($"{remoteActorSystem}/{remoteActor}",
                (an) =>
                {
                    isNew = true;
                    var p = new RemoteProcess(remoteActor, this, 
                            new RemoteAddress(options.EndPoint, remoteActorSystem, remoteActor));

                    return new ProcessRegistery(p, typeof(RemoteProcess), ProcessType.Remote);
                });

            if (!isNew &&
                (registery.ProcessType != ProcessType.Remote) || (registery.ActorType != typeof(RemoteProcess)))
                throw new Exception(Errors.ActorWithNameOfDifferentTypeAlreadyExists);

            return registery.Process.Pid;
        }

        void IResponseHandler.OnResponse(IMessage message, RemoteAddress from)
        {
            throw new NotImplementedException();
        }
    }
}
