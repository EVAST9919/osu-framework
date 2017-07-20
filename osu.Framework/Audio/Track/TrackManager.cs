﻿// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using osu.Framework.IO.Stores;

namespace osu.Framework.Audio.Track
{
    public class TrackManager : AudioCollectionManager<Track>
    {
        private readonly IResourceStore<byte[]> store;

        public TrackManager(IResourceStore<byte[]> store)
        {
            this.store = store;
        }

        public Track Get(string name)
        {
            TrackBass track = new TrackBass(store.GetStream(name));
            AddItem(track);
            return track;
        }
    }
}
