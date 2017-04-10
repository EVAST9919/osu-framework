﻿// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using System;
using System.Runtime.InteropServices;

namespace osu.Framework.Desktop.Platform.Windows.Native
{
    internal static class Execution
    {
        [DllImport("kernel32.dll")]
        internal static extern uint SetThreadExecutionState(ExecutionState state);

        [Flags]
        internal enum ExecutionState : uint
        {
            AwaymodeRequired = 0x00000040,
            Continuous = 0x80000000,
            DisplayRequired = 0x00000002,
            SystemRequired = 0x00000001,
            UserPresent = 0x00000004,
        }
    }
}
