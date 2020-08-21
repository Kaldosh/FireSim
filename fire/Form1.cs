using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace fire
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            for (int i = 0; i < Wid; i++)
            {
                buff[Siz + i] = 220;
            }


            colors = new Color[256];
            int j = 0;
            for (int i = 0; i < 64; i++) { colors[j++] = Color.FromArgb(i << 2, 0, 0); }
            for (int i = 0; i < 64; i++) { colors[j++] = Color.FromArgb(255, i << 2, 0); }
            for (int i = 0; i < 64; i++) { colors[j++] = Color.FromArgb(255, 255, i << 2); }
            for (int i = 0; i < 64; i++) { colors[j++] = Color.FromArgb(255 - i, 255 - i, 255); }

            brushes = colors.Select(x => new SolidBrush(x)).ToArray();

            //bmp = new Bitmap(Wid, Hei);
            bmp = new Bitmap(Wid << ScalePow, Hei << ScalePow);
            gfx = System.Drawing.Graphics.FromImage(bmp);

            KeepRunning = true;
            //not threadsafe, but it should look ok anyway?
            threads = Enumerable.Range(0, NumThreads).Select(x => new Thread(() => RunBurn())).ToArray();
            foreach (var t in threads) { t.Start(); }



        }


        Bitmap bmp;
        Graphics gfx;

        Color[] colors;
        SolidBrush[] brushes;
        Thread[] threads;

        long LastBurnMS;
        long LastFrameMS;
        System.Diagnostics.Stopwatch LastFrameSw = System.Diagnostics.Stopwatch.StartNew();

        volatile int RollBurnPtr = 0;
        volatile int RollDrawPtr = 0;
        const int RollingAvgLen = 100;
        long[] RollingBurnTimes = new long[RollingAvgLen];
        long[] RollingDrawTimes = new long[RollingAvgLen];

        const int NumThreads = 4;

        const int WidPow = 8;
        const int HeiPow = 8;
        const int Wid = 1 << WidPow;
        const int WidMask = Wid - 1; //1 less thna a power of 2 is a mask for the maxvalue (e.g. 256-1=255=0b11111111)
        const int Hei = 1 << HeiPow;
        const int Siz = Wid * Hei;
        const int ScalePow = 1;

        const int ZonePow = 4;

        byte[] buff = new byte[Siz + Wid + 1];//only use siz; but have read-only zone beyond the end, full of fire (and avoid out-of-bounds)
        bool KeepRunning;

        private void RunBurn()
        {
            Random rnd = new Random();
            while (KeepRunning)
            {
                BurnZones(rnd);
                //BurnFull(rnd);
            }
        }

        private void tmrMain_Tick(object sender, EventArgs e)
        {
            //Burn();
            LastFrameMS = LastFrameSw.ElapsedMilliseconds;
            LastFrameSw.Restart();
            picMain.Invalidate();
        }

        long TotIts = 0;
        private void BurnZones(Random rnd)
        {
            var st = System.Diagnostics.Stopwatch.StartNew();
            for (int zoneloop = 0; zoneloop < 10; zoneloop++)
            {
                long its = 0;
                for (int zone = 0; zone < (Hei >> ZonePow); zone++)
                {
                    int top = zone << WidPow << ZonePow;
                    int bot = zone + 1 << WidPow << ZonePow;
                    for (int i = 0; i < 1000; i++)
                    {
                        IterateBurnZone(rnd, top, bot);
                        IterateBurnZone(rnd, top, bot);
                        IterateBurnZone(rnd, top, bot);
                        IterateBurnZone(rnd, top, bot);
                        its += 4;
                    }
                }
                Interlocked.Add(ref TotIts, its);
            }
            LastBurnMS = st.ElapsedMilliseconds;
        }
        private void BurnFull(Random rnd)
        {
            var st = System.Diagnostics.Stopwatch.StartNew();
            var its = 0;
            for (int i = 0; i < 40000; i++)
            {
                IterateBurn(rnd);
                IterateBurn(rnd);
                IterateBurn(rnd);
                IterateBurn(rnd);
                its += 4;
            }
            Interlocked.Add(ref TotIts, its);
            LastBurnMS = st.ElapsedMilliseconds;
        }

        [MethodImplAttribute(MethodImplOptions.AggressiveInlining)]
        private void IterateBurnZone(Random rnd, int Top, int Bot)
        {
            var WriteIndex = rnd.Next(Top, Bot); //write within the block size; read from slightly below it
            var r = buff[WriteIndex + Wid + rnd.Next(-1, 2)] - rnd.Next(-8, 12); //subtract some (or add a small amount sometimes)
            r &= (~r >> 8);//if it became negative, then the next more sig byte will be all 1's;  so and it with the not; will zero it if it crossed zero - this speeds up the inner loop vs an IF, due to branch misprediction. ; and exceeding 255 is fine, it looks pretty to have black spots
            buff[WriteIndex] = (byte)r; //write the result to one span earlier (i.e. previous line), plus or minus 1 for left/right drift.
        }

        [MethodImplAttribute(MethodImplOptions.AggressiveInlining)]
        private void IterateBurn(Random rnd)
        {
            var p = rnd.Next(Wid + 1, Siz);
            var r = buff[p] - rnd.Next(-8, 12); //subtract some (or add a small amount sometimes)
            r &= (~r >> 8);//if it became negative, then the next more sig byte will be all 1's;  so and it with the not; will zero it if it crossed zero - this speeds up the inner loop vs an IF, due to branch misprediction. ; and exceeding 255 is fine, it looks pretty to have black spots
            buff[p - Wid - rnd.Next(-1, 2)] = (byte)r; //write the result to one span earlier (i.e. previous line), plus or minus 1 for left/right drift.
        }



        private void picMain_Paint(object sender, PaintEventArgs e)
        {
            //e.Graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
            //e.Graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;

            var st = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < Siz; i++)
            {
                e.Graphics.FillRectangle(brushes[buff[i]], (i & WidMask) << ScalePow, (i >> WidPow) << ScalePow, 1 << ScalePow, 1 << ScalePow);
                //gfx.FillRectangle(brushes[buff[i]], (i & WidMask) << ScalePow, (i >> WidPow) << ScalePow, 1 << ScalePow, 1 << ScalePow);
                //bmp.SetPixel((i & WidMask), (i >> WidPow), colors[buff[i]]);
            }
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            //e.Graphics.DrawImage(bmp, 0, 0, Wid << ScalePow, Hei << ScalePow);
            var LastDrawTime = st.ElapsedMilliseconds;
            var ptr = Interlocked.Increment(ref RollDrawPtr) % RollingAvgLen;
            RollingDrawTimes[ptr] = LastDrawTime;

            if (ptr > ((10 + NumThreads) * RollingAvgLen)) Interlocked.Add(ref RollDrawPtr, -RollingAvgLen); //can go over threshold by numthreads of inc; and under by numthreads*rollLen of subtract; but never overflow, and never negative
            var AvgDraw = RollingDrawTimes.Average();//not threadsafe, but it'l usually snapshot something with meaningful values even if some out of sequence
            var SeenIts = Interlocked.Read(ref TotIts);
            Interlocked.Add(ref TotIts, -SeenIts);
            e.Graphics.DrawString($"Burn:{LastBurnMS:000};Interframe:{LastFrameMS:000};Draw:{LastDrawTime:000}(avg:{AvgDraw:000}); Its:({SeenIts/(LastFrameMS*1000f):000.000}M/sec)={SeenIts}", Form1.DefaultFont, brushes[255], 0, 0);

        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            KeepRunning = false;
        }
    }
}
