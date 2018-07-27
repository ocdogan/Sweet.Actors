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
            public Type ActorType;
            public Process Process;
            public ProcessType ProcessType = ProcessType.Class;

            public void Dispose()
            {
                using (Interlocked.Exchange(ref Process, null)) { }
            }
        }

        private class AnonymousNameGenerator : Id<ActorSystem>
        {
            public AnonymousNameGenerator(int major, int majorRevision, int minor, int minorRevision)
                : base(major, majorRevision, minor, minorRevision)
            { }

            public static string Next()
            {
                var buffer = Generate();
                return new String(AsChars(Common.ProcessId, buffer[0], buffer[1], buffer[2], buffer[3]));
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
            Name = Options.Name?.Trim();
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
                foreach (var registry in processes.Values)
                {
                    try
                    {
                        registry.Dispose();
                    }
                    catch (Exception)
                    { }
                }
            }
        }

        internal void SetRemoteManager(IRemoteManager remoteManager)
        {
            _remoteManager = remoteManager;
        }

        public static bool TryGet(string actorSystemName, out ActorSystem actorSystem)
        {
            return _systemRegistery.TryGetValue(actorSystemName?.Trim(), out actorSystem);
        }

        public static bool TryGet(Aid aid, out Pid pid)
        {
            pid = null;
            if (aid != null)
            {
                pid = aid as Pid;
                if (pid?.Process != null)
                    return true;

                if (TryGet(aid.ActorSystem, out ActorSystem actorSystem))
                    return actorSystem.TryGetInternal(aid.Actor, out pid) ||
                        actorSystem.TryGetInternal($"remote://{aid.ActorSystem}/{aid.Actor}", out pid);
            }
            return false;
        }

        public bool TryGetLocal(Aid localAid, out Pid pid)
        {
            pid = null;
            if (localAid != null)
            {
                pid = localAid as Pid;
                if (pid?.Process != null)
                    return true;

                return TryGetInternal(localAid.Actor, out pid);
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

                return TryGetInternal($"remote://{remoteAid.ActorSystem}/{remoteAid.Actor}", out pid);
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

            if (!String.IsNullOrEmpty(actorName))
                return TryGetInternal(actorName, out pid);
            return false;
        }

        internal bool TryGetInternal(string actorName, out Pid pid)
        {
            pid = null;
            if (_processRegistery.TryGetValue(actorName, out ProcessRegistery registry))
            {
                pid = registry.Process?.Pid;
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
            return (options?.EndPoint != null) &&
                !String.IsNullOrEmpty(options?.RemoteActorSystem?.Trim());
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
            var processType = ProcessType.Class;

            var registry = _processRegistery.GetOrAdd(actorName,
                (an) =>
                {
                    isNew = true;

                    var p = new Process(name: an,
                                actorSystem: this,
                                actor: actor ?? (IActor)Activator.CreateInstance(actorType),
                                options: options);

                    return new ProcessRegistery {
                        Process = p,
                        ActorType = actorType,
                        ProcessType = processType
                    };
                });

            if (!isNew && 
                ((registry.ProcessType != processType) || (registry.ActorType != actorType)))
                throw new Exception(Errors.ActorWithNameOfDifferentTypeAlreadyExists);

            return registry.Process.Pid;
        }

        private int? GetRequestTimeoutMSec(ActorOptions options)
        {
            var result = options.RequestTimeoutMSec;
            if (!result.HasValue || result == -1)
                result = Options.RequestTimeoutMSec;

            return result;
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

            var actorType = typeof(FunctionCallProcess);
            var processType = ProcessType.Function;

            var registry = _processRegistery.GetOrAdd(actorName,
                (an) =>
                {
                    isNew = true;

                    var p = new FunctionCallProcess(name: actorName, 
                                actorSystem: this, 
                                function: receiveFunc,
                                options: options);

                    return new ProcessRegistery {
                        Process = p,
                        ActorType = actorType,
                        ProcessType = processType
                    };
                });

            if (!isNew)
            {
                if (registry.ProcessType != processType)
                    throw new Exception(Errors.ActorWithNameOfDifferentTypeAlreadyExists);

                var fp = registry.Process as FunctionCallProcess;
                if (fp == null)
                    throw new Exception(Errors.ActorWithNameOfDifferentTypeAlreadyExists);

                if (fp.ReceiveFunc != receiveFunc)
                    throw new Exception(Errors.ActorAlreadyExsists);
            }

            return registry.Process.Pid;
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

            var actorType = typeof(RemoteProcess);
            var processType = ProcessType.Remote;

            var registry = _processRegistery.GetOrAdd($"remote://{remoteActorSystem}/{remoteActor}",
                (an) =>
                {
                    isNew = true;
                    var p = new RemoteProcess(name: remoteActor, 
                                actorSystem: this, 
                                remoteAddress: new RemoteAddress(options.EndPoint, remoteActorSystem, remoteActor),
                                options: options);

                    return new ProcessRegistery {
                        Process = p,
                        ActorType = actorType,
                        ProcessType = processType
                    };
                });

            if (!isNew &&
                (registry.ProcessType != processType) || (registry.ActorType != actorType))
                throw new Exception(Errors.ActorWithNameOfDifferentTypeAlreadyExists);

            return registry.Process.Pid;
        }

        void IResponseHandler.OnResponse(IMessage message, RemoteAddress from)
        {
            throw new NotImplementedException();
        }
    }
}
