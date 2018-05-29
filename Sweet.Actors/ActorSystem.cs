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
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public sealed class ActorSystem
    {
        private class FunctionCallActor : IActor
        {
            private Func<IContext, IMessage, Task> _receiveFunc;

            public FunctionCallActor(Func<IContext, IMessage, Task> receiveFunc)
            {
                _receiveFunc = receiveFunc;
            }

            public Task OnReceive(IContext ctx, IMessage msg)
            {
                return _receiveFunc(ctx, msg);
            }
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

        private readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Process>> _actorRegistery =
            new ConcurrentDictionary<Type, ConcurrentDictionary<string, Process>>();

        private readonly ConcurrentDictionary<string, Process> _functionRegistery =
            new ConcurrentDictionary<string, Process>();

        private ActorSystem(ActorSystemSettings settings)
        {
            SystemSettings = settings;
            Name = settings.Name;
        }

        public string Name { get; }

        public ActorSystemSettings SystemSettings { get; }

        public static ActorSystem GetOrAdd(ActorSystemSettings settings = null)
        {
            settings = (settings ?? ActorSystemSettings.Default);
            return _systemRegistery.GetOrAdd(settings.Name,
                (sn) => new ActorSystem(settings));
        }

        public Pid FromType<T>(ActorSettings settings = null)
            where T : class, IActor, new()
        {
            var processList =
                _actorRegistery.GetOrAdd(typeof(T),
                                         (t) => new ConcurrentDictionary<string, Process>());

            settings = (settings ?? ActorSettings.Default);

            var p = processList.GetOrAdd(GetActorName(settings),
                (an) =>
                {
                    var sequentialInvokeLimit = settings.SequentialInvokeLimit;
                    if (sequentialInvokeLimit < 1)
                        sequentialInvokeLimit = SystemSettings.SequentialInvokeLimit;

                    var actor = Activator.CreateInstance<T>();
                    return new Process(this, actor, null, GetSequentialInvokeLimit(settings), settings.InitialContextData);
                });

            return p.Pid;
        }

        private static string GetActorName(ActorSettings settings)
        {
            var result = settings.Name?.Trim();
            if (String.IsNullOrEmpty(result))
                result = Constants.EmptyActorName;

            return result;
        }

        public (bool, Pid) FromActor(IActor actor, ActorSettings settings = null)
        {
            if (actor == null)
                throw new ArgumentNullException(nameof(actor));

            var processList =
                _actorRegistery.GetOrAdd(actor.GetType(),
                                         (t) => new ConcurrentDictionary<string, Process>());

            settings = (settings ?? ActorSettings.Default);

            var exists = true;
            var p = processList.GetOrAdd(GetActorName(settings),
                (an) =>
                {
                    exists = false;
                    return new Process(this, actor, null, GetSequentialInvokeLimit(settings), settings.InitialContextData);
                });

            return (!exists, p.Pid);
        }

        public Pid FromFunction(Func<IContext, IMessage, Task> receiveFunc, ActorSettings settings = null)
        {
            if (receiveFunc == null)
                throw new ArgumentNullException(nameof(receiveFunc));

            settings = (settings ?? ActorSettings.Default);

            var actorName = settings.Name?.Trim();
            if (String.IsNullOrEmpty(actorName))
                actorName = AnonymousNameGenerator.Next();

            lock (_actorRegistery)
            {
                if (_functionRegistery.ContainsKey(actorName))
                    throw new Exception(String.Format(Errors.ActorAlreadyExsists, actorName));

                var actor = new FunctionCallActor(receiveFunc);
                var p = new Process(this, actor, null, GetSequentialInvokeLimit(settings), settings.InitialContextData);

                _functionRegistery[actorName] = p;
                return p.Pid;
            }
        }

        private int GetSequentialInvokeLimit(ActorSettings settings)
        {
            var result = settings.SequentialInvokeLimit;
            if (result < 1)
                result = SystemSettings.SequentialInvokeLimit;

            return result;
        }
    }
}
