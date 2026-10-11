using System;

// Mirrors Assets/StreamingAssets/burrow/burrow.json written by tools/burrow/bake.py.
// Parsed with JsonUtility, so everything is flat primitives / primitive arrays.
namespace Burrow
{
    [Serializable]
    public class BParams
    {
        public float ceiling, sculpt_top, throat_y, throat_r, flare_r, blanket_r, r_min, r_max;
    }

    [Serializable]
    public class BNode
    {
        public int id;
        public string kind;      // mind | root | sculpture | spur
        public int mind;         // index into minds, -1 for non-mind nodes
        public int deg;
        public float nod_r, dome_r;
        public float[] p, face, dome;
    }

    [Serializable]
    public class BRun
    {
        public int id, a, b;     // a = upstream node (older), b = downstream (toward the sculpture)
        public string kind;      // chain | cross | spur | tendril
        public float r, len, flow;
        public float[] sp;       // coarse spine, flat x,y,z triples, a -> b
    }

    [Serializable]
    public class BMind
    {
        public int id;
        public string name;
        public int flask, gen;
        public float fitness, mut;
        public float[] pc;       // first genome principal components, roughly -1..1
    }

    [Serializable]
    public class BBounds { public float[] min, max; }

    [Serializable]
    public class BurrowManifest
    {
        public int version, seed;
        public BParams @params;
        public BBounds bounds;
        public BNode[] nodes;
        public BRun[] runs;
        public BMind[] minds;
    }

    [Serializable]
    public class PoemFile { public string[] poems; }
}
