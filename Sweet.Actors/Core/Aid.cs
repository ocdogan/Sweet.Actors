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
using System.Text;

namespace Sweet.Actors
{
    public class Aid
    {
        public static Aid Unknown => new Aid();

        private int _hashCode;
        private string _actor;
        private string _actorSystem;

        protected Aid()
        {
            _actor = String.Empty;
            _actorSystem = String.Empty;
        }

        public Aid(string actorSystem, string actor)
        {
            _actor = actor?.Trim();
            if (String.IsNullOrEmpty(_actor))
                throw new ArgumentNullException(nameof(actor));

            _actorSystem = actorSystem?.Trim();
            if (String.IsNullOrEmpty(_actorSystem))
                throw new ArgumentNullException(nameof(actorSystem));
        }

        public string Actor => _actor;
        
        public string ActorSystem => _actorSystem;

        protected void SetActor(string actor)
        {
            actor = actor?.Trim();
            if (actor != _actor)
            {
                _actor = actor;
                _hashCode = 0;
            }
        }

        protected void SetActorSystem(string actorSystem)
        {
            actorSystem = actorSystem?.Trim();
            if (actorSystem != _actorSystem)
            {
                _actorSystem = actorSystem;
                _hashCode = 0;
            }
        }

        public override string ToString()
        {
            //$"{_actorSystem}/{_actor}";
            return _actorSystem + "/" + _actor;
        } 

        public override int GetHashCode()
        {
            if (_hashCode == 0)
            {
                var hash = 1 + (_actorSystem?.GetHashCode() ?? 0);
                _hashCode = 31 * hash + (_actor?.GetHashCode() ?? 0);
            }
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            
            if (obj is Aid ra)
                return (ra.GetHashCode() == GetHashCode()) &&
                    ra._actorSystem == _actorSystem &&
                    ra._actor == _actor;
            return false;
        }

        public static Aid Parse(string str)
        {
            if (!(String.IsNullOrEmpty(str) || (str == "/")))
            {
                var slashPos1 = str.IndexOf('/', 0);
                if (slashPos1 > -1)
                {
                    var actorSystem = str.Substring(0, slashPos1)?.Trim();
                    if (!String.IsNullOrEmpty(actorSystem))
                    {
                        slashPos1++;
                        if (slashPos1 < str.Length - 1)
                        {
                            var slashPos2 = str.IndexOf('/', slashPos1);
                            if (slashPos2 == -1)
                                slashPos2 = str.Length;

                            var subLen = slashPos2 - slashPos1;
                            if (subLen > 0)
                            { 
                                var actor = str.Substring(slashPos1, subLen)?.Trim();
                                if (!String.IsNullOrEmpty(actor))
                                    return new Aid(actorSystem, actor);
                            }
                        }
                    }
                }
            }
            return Unknown;
        }
    }
}