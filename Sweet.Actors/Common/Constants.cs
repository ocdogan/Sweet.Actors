﻿#region License
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

namespace Sweet.Actors
{
    public static class Constants
    {
        public const int DefaultPort = 0;
        public const string DefaultHost = "local";

        public const string Protocol = "playbook://";
        public const string AddressFormat = "playbook://{0}:{1}/{2}";

        public const string EmptyActorName = "[FAE04EC0-301F-11D3-BF4B-00C04F79EFBC]";
        public const string DefaultActorSystemName = "[EDF53DA8-5449-4489-BDFC-CF165071362A]";

        public const int MinSequentialInvokeLimit = 50;
        public const int MaxSequentialInvokeLimit = 2000;
        public const int DefaultSequentialInvokeLimit = 240;

        public static readonly int ProtocolLength = Protocol.Length;
        public static readonly int EmptyProtocolLength = "playbook://:0/".Length;
    }
}