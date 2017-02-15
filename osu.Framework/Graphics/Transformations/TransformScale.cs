// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

namespace osu.Framework.Graphics.Transformations
{
    public class TransformScale : TransformVector
    {
        public override void Apply(Drawable d)
        {
            base.Apply(d);
            d.Scale = CurrentValue;
        }
    }
}
