﻿// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using System;

namespace osu.Framework.Graphics.Containers
{
    /// <summary>
    /// A container which is rounded (via automatic corner-radius) on the shortest edge.
    /// </summary>
    public class CircularContainer : Container
    {
        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            CornerRadius = Math.Min(DrawSize.X, DrawSize.Y) / 2f;
        }
    }
}
