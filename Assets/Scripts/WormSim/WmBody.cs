// sim/worm.py — the trailing IK chain and the steered head (body.c).

namespace WormSim
{
    public sealed class WmBody
    {
        // Segment i runs from head (Hx[i], Hy[i]) to tail (Tx[i], Ty[i]); the
        // midline a renderer wants is Hx/Hy in order (Hx[0] is the worm's head).
        public readonly double[] Hx = new double[WmConst.NSegments];
        public readonly double[] Hy = new double[WmConst.NSegments];
        public readonly double[] Tx = new double[WmConst.NSegments];
        public readonly double[] Ty = new double[WmConst.NSegments];

        public double FacingDir, TargetDir, Speed, TargetSpeed, SpeedChange;
        public double TargetX, TargetY;

        public WmBody(double ox, double oy, WmRng r)
        {
            TargetX = ox;
            TargetY = oy;

            // IKChain(facing=None): four uniform(-1, 1) per link, in this exact
            // order. 200 links = 800 draws, and they come before RandExcite's 40.
            double x = ox, y = oy;
            for (int i = 0; i < WmConst.NSegments; i++)
            {
                double hx = x + r.Uniform(-1.0, 1.0);
                double hy = y + r.Uniform(-1.0, 1.0);
                double tx = hx + r.Uniform(-1.0, 1.0);
                double ty = hy + r.Uniform(-1.0, 1.0);
                Hx[i] = hx; Hy[i] = hy;
                Tx[i] = tx; Ty[i] = ty;
                x = tx; y = ty;
            }
        }

        public void ConsumeMotor(double accumLeft, double accumRight)
        {
            const double scaling = 20.0;
            double newDir = (accumLeft - accumRight) / scaling;
            TargetDir = FacingDir + newDir * WmConst.Pi;
            TargetSpeed = (WmMath.Fabs(accumLeft) + WmMath.Fabs(accumRight)) / (scaling * 5.0);
            SpeedChange = (TargetSpeed - Speed) / (scaling * 1.5);
        }

        private void ChainUpdate(double targetX, double targetY)
        {
            double[] hx = Hx, hy = Hy, tx = Tx, ty = Ty;

            hx[0] = targetX;
            hy[0] = targetY;
            // hx[1:] = tx[:-1] — each segment's head becomes the previous
            // segment's PREVIOUS-tick tail.
            for (int i = WmConst.NSegments - 1; i >= 1; i--)
            {
                hx[i] = tx[i - 1];
                hy[i] = ty[i - 1];
            }

            const double size = WmConst.SegmentSize;
            const double strength = 0.998;
            for (int i = 0; i < WmConst.NSegments; i++)
            {
                double dx = hx[i] - tx[i];
                double dy = hy[i] - ty[i];
                double dist = WmMath.Sqrt(dx * dx + dy * dy);
                if (dist == 0.0) dist = 1e-9;  // mirrors the scalar `... or 1e-9`
                double force = (0.5 - (size / dist) * 0.5) * 0.99;
                double fx = force * dx;
                double fy = force * dy;
                tx[i] += fx * strength * 2.0;
                ty[i] += fy * strength * 2.0;
                hx[i] -= fx * (1.0 - strength) * 2.0;
                hy[i] -= fy * (1.0 - strength) * 2.0;
            }
        }

        public void Step()
        {
            Speed += SpeedChange;
            // Smallest signed angle facing->target. Python's % is floored, so
            // this is PyMod, not %.
            double diff = WmMath.PyMod(TargetDir - FacingDir + WmConst.Pi, 2.0 * WmConst.Pi) - WmConst.Pi;
            if (diff > 0.0) FacingDir += 0.1;
            else if (diff < 0.0) FacingDir -= 0.1;

            // worm-sim used screen coords (y down): target.y -= sin(facing) * speed.
            // v7 kept the sign and lets the renderer decide which way y points.
            TargetX += WmMath.Cos(FacingDir) * Speed;
            TargetY -= WmMath.Sin(FacingDir) * Speed;

            ChainUpdate(TargetX, TargetY);
        }
    }
}
