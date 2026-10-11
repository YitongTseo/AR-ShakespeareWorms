// WormSim — a C# port of HamletWorms/firmware/components/wormsim (itself a port
// of HamletRNAWorld v7's sim/). Plain .NET, no UnityEngine dependency, so the
// same sources compile under Unity's Mono (tools/wormsim_test/run.sh) and can
// be diffed tick for tick against the C hostsim.
//
// Everything is `double`, deliberately: the connectome fires on a hard
// `psyn > 30` threshold, so a float32 anywhere in the chain flips a neuron
// within seconds and the worm walks away from its C/Python twins.

namespace WormSim
{
    public static class WmConst
    {
        // --- world constants (sim/world.py, sim/text_scroller.py) ---------------
        public const double WorldW = 1600.0;
        public const double WorldH = 1000.0;
        public const int BodyTickHz = 60;
        public const int BrainTickPeriod = 30;
        public const double BodyDt = 1.0 / 60.0;
        public const double FoodSenseRadius = 200.0;
        public const double FoodEatRadius = 20.0;
        public const int StimLingerTicks = 120;  // int(2.0 * 60)

        public const double ScrollSpeed = 15.0;
        public const double SpawnY = WorldH - 20.0;
        public const double KillY = -80.0;
        public const double SpawnInterval = 4.5;
        public const double CharW = 11.0;
        public const double WordGap = 70.0;
        public const double CenterX = WorldW / 2.0;

        public const int NSegments = 200;
        public const double SegmentSize = 4.0;
        public const double FireThreshold = 30.0;

        // --- embedding dimensions (server/embedding.py) ------------------------
        public const int DEmb = 11;
        public const int History = 5;
        public const int DHistConcat = DEmb * History;  // 55
        public const int DFuse = DEmb * 2;              // 22
        public const int NTags = 12;
        public const int DPosIn = NTags * (History + 1); // 72
        public const int DPosHid = 16;
        public const int NPc = 12;  // 11 embedding dims + 1 POS dim

        public const ushort Oov = 0xFFFF;

        // Scroller slot limits, mirrored from the firmware so the baked corpus
        // behaves identically (bake.py refuses a corpus that would exceed them).
        public const int MaxActiveLines = 24;
        public const int MaxLineWords = 40;

        public const double Pi = 3.141592653589793;
    }
}
