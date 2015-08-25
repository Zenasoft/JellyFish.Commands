﻿// Copyright (c) Zenasoft. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Jellyfish.Commands.Utils
{
    public interface IClock
    {
        long EllapsedTimeInMs { get; }
    }

    public sealed class Clock :IClock
    {
        private Stopwatch _sw = new Stopwatch();
        private long _startTimeInMs;
        private static IClock _instance = new Clock();

        public static IClock GetInstance() { return _instance; }

        private Clock()
        {
            _sw.Start();
            _startTimeInMs = (long)(DateTime.Now - DateTime.MinValue).TotalMilliseconds;
        }

        public long EllapsedTimeInMs
        {
            get { return _sw.ElapsedMilliseconds; }
        }

      
    }
}
