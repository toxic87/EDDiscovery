﻿/*
 * Copyright 2019-2023 Robbyxp1 @ github.com
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software distributed under
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF
 * ANY KIND, either express or implied. See the License for the specific language
 * governing permissions and limitations under the License.
 */

using EDDiscovery.Forms;
using EliteDangerousCore;
using EliteDangerousCore.EDSM;
using EliteDangerousCore.GMO;
using GLOFC;
using GLOFC.Controller;
using GLOFC.GL4;
using GLOFC.GL4.Controls;
using GLOFC.GL4.Operations;
using GLOFC.GL4.Shaders;
using GLOFC.GL4.Shaders.Compute;
using GLOFC.GL4.Shaders.Fragment;
using GLOFC.GL4.Shaders.Sprites;
using GLOFC.GL4.ShapeFactory;
using GLOFC.GL4.Textures;
using GLOFC.Utils;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static GLOFC.GL4.Controls.GLBaseControl;

namespace EDDiscovery.UserControls.Map3D
{
    public interface MapSaver           // saver interface
    {
        void PutSetting<T>(string id, T value);
        T GetSetting<T>(string id, T defaultvalue);
        void DeleteSetting(string id);
    }

    public class Map
    {
        public Controller3D gl3dcontroller;
        public GLControlDisplay displaycontrol;
        public enum Parts       // select whats displayed
        {
            None = 0,
            Galaxy = (1 << 0),
            GalObjects = (1 << 1),
            Regions = (1 << 2),
            Grid = (1 << 3),
            StarDots = (1 << 4),
            TravelPath = (1 << 5),
            GalaxyStars = (1 << 6),
            NavRoute = (1 << 7),
            Route = (1 << 8),
            Bookmarks = (1 << 9),
            ImageList = (1<<10),

            Menu = (1<<16),
            RightClick = (1 << 17),
            SearchBox = (1 << 18),
            GalaxyResetPos = (1 << 19),
            PerspectiveChange = (1 << 20),
            YHoldButton = (1 << 21),
            LimitSelector = (1<<22),

            AutoEDSMStarsUpdate = (1 << 28),
            PrepopulateGalaxyStarsLocalArea = (1 << 29),

            Map3D = 0x10ffffff,
            //Map3D = 0x010ff0001,
        }

        public Action<List<string>> AddSystemsToExpedition;

        private Parts parts;

        private GLMatrixCalc matrixcalc;
        private GLOFC.WinForm.GLWinFormControl glwfc;

        private GLRenderProgramSortedList rObjects = new GLRenderProgramSortedList();
        private GLItemsList items = new GLItemsList();

        private Vector4[] volumetricboundingbox;
        private GLVolumetricUniformBlock volumetricblock;
        private GLRenderableItem galaxyrenderable;
        private GalaxyShader galaxyshader;

        private ImageCache userimages;
        private GLOFC.GL4.Bitmaps.GLBindlessTextureBitmaps usertexturebitmaps;

        private GLShaderPipeline gridtextshader;
        private GLShaderPipeline gridshader;
        private GLRenderableItem gridrenderable;

        private TravelPath travelpath;
        private TravelPath routepath;
        private TravelPath navroute;

        public GalacticMapping edsmmapping;

        private GalMapObjects galmapobjects;
        private GalMapRegions edsmgalmapregions;
        private GalMapRegions elitemapregions;

        private GalaxyStarDots stardots;
        private GLPointSpriteShader starsprites;
        private GalaxyStars galaxystars;

        private Bookmarks bookmarks;

        private GLContextMenu rightclickmenu;

        private MapMenu galaxymenu;

        // global buffer blocks used
        private const int volumenticuniformblock = 2;
        private const int findblock = 3;
        private const int userbitmapsarbblock = 4;

        private System.Diagnostics.Stopwatch hptimer = new System.Diagnostics.Stopwatch();

        private int localareasize = 50;
        private UserControlCommonBase parent;

        private int autoscalegmo = 30;
        private int autoscalebookmarks = 30;
        private int autoscalegalstars = 30;

        private bool mapcreatedokay = false; 

        public Map()
        {
        }

        public void Dispose()
        {
            if (galaxystars != null)
                galaxystars.Stop();
            items.Dispose();
            
           // GLStatics.VerifyAllDeallocated(); // enable during debugging only - will except if multiple windows are open
        }

        public ulong ElapsedTimems { get { return glwfc.ElapsedTimems; } }

        #region Initialise
        public bool Start(GLOFC.WinForm.GLWinFormControl glwfc, GalacticMapping edsmmapping, GalacticMapping eliteregions, UserControlCommonBase parent, Parts parts)
        {
            this.parent = parent;

            this.parts = parts;
            this.glwfc = glwfc;
            this.edsmmapping = edsmmapping;

            hptimer.Start();

            GLShaderLog.Reset();
            GLShaderLog.AssertOnError = false;

            items.Add(new GLMatrixCalcUniformBlock(), "MCUB");     // create a matrix uniform block 

            int lyscale = 1;
            int front = -20000 / lyscale, back = front + 90000 / lyscale, left = -45000 / lyscale, right = left + 90000 / lyscale, vsize = 2000 / lyscale;

            // parts = parts - (1 << 9);

            System.Diagnostics.Debug.Assert(glwfc.IsContextCurrent());

            Bitmap galaxybitmap = BaseUtils.Icons.IconSet.GetBitmap("GalMap.Galaxy_L180");

            if ((parts & Parts.ImageList) != 0)
            {
                userimages = new ImageCache(items, rObjects);
                usertexturebitmaps = new GLOFC.GL4.Bitmaps.GLBindlessTextureBitmaps("UserBitmaps", rObjects, userbitmapsarbblock, false);
                items.Add(usertexturebitmaps);
            }

            if ((parts & Parts.Galaxy) != 0) // galaxy
            {
                volumetricboundingbox = new Vector4[]
                    {
                        new Vector4(left,-vsize,front,1),
                        new Vector4(left,vsize,front,1),
                        new Vector4(right,vsize,front,1),
                        new Vector4(right,-vsize,front,1),

                        new Vector4(left,-vsize,back,1),
                        new Vector4(left,vsize,back,1),
                        new Vector4(right,vsize,back,1),
                        new Vector4(right,-vsize,back,1),
                    };


                const int gnoisetexbinding = 3;     //tex bindings are attached per shaders so are not global
                const int gdisttexbinding = 4;
                const int galtexbinding = 1;

                volumetricblock = new GLVolumetricUniformBlock(volumenticuniformblock);
                items.Add(volumetricblock, "VB");

                int sc = 1;
                GLTexture3D noise3d = new GLTexture3D(1024 * sc, 64 * sc, 1024 * sc, OpenTK.Graphics.OpenGL4.SizedInternalFormat.R32f); // red channel only
                items.Add(noise3d, "Noise");
                ComputeShaderNoise3D csn = new ComputeShaderNoise3D(noise3d.Width, noise3d.Height, noise3d.Depth, 128 * sc, 16 * sc, 128 * sc, gnoisetexbinding);       // must be a multiple of localgroupsize in csn
                csn.StartAction += (A, m) => { noise3d.BindImage(gnoisetexbinding); };
                csn.Run();      // compute noise
                csn.Dispose();  // and finish with it

                GLTexture1D gaussiantex = new GLTexture1D(1024, OpenTK.Graphics.OpenGL4.SizedInternalFormat.R32f); // red channel only
                items.Add(gaussiantex, "Gaussian");

                // set centre=width, higher widths means more curve, higher std dev compensate.
                // fill the gaussiantex with data
                ComputeShaderGaussian gsn = new ComputeShaderGaussian(gaussiantex.Width, 2.0f, 2.0f, 1.4f, gdisttexbinding);
                gsn.StartAction += (A, m) => { gaussiantex.BindImage(gdisttexbinding); };
                gsn.Run();      // compute noise
                gsn.Dispose();

                GL.MemoryBarrier(MemoryBarrierFlags.AllBarrierBits);

                // load one upside down and horz flipped, because the volumetric co-ords are 0,0,0 bottom left, 1,1,1 top right
                GLTexture2D galtex = new GLTexture2D(galaxybitmap, SizedInternalFormat.Rgba8);
                items.Add(galtex, "galtex");
                galaxyshader = new GalaxyShader(volumenticuniformblock, galtexbinding, gnoisetexbinding, gdisttexbinding);
                items.Add(galaxyshader, "Galaxy-sh");
                // bind the galaxy texture, the 3dnoise, and the gaussian 1-d texture for the shader
                galaxyshader.StartAction += (a, m) => { galtex.Bind(galtexbinding); noise3d.Bind(gnoisetexbinding); gaussiantex.Bind(gdisttexbinding); };      // shader requires these, so bind using shader

                GLRenderState rt = GLRenderState.Tri();
                galaxyrenderable = GLRenderableItem.CreateNullVertex(OpenTK.Graphics.OpenGL4.PrimitiveType.Points, rt);   // no vertexes, all data from bound volumetric uniform, no instances as yet
                rObjects.Add(galaxyshader, "galshader", galaxyrenderable);
            }

            if ((parts & Parts.Regions) != 0)
            {
                var corr = new GalMapRegions.ManualCorrections[] {          // nerf the centeroid position slightly
                    new GalMapRegions.ManualCorrections("The Galactic Aphelion", y: -2000 ),
                    new GalMapRegions.ManualCorrections("The Abyss", y: +3000 ),
                    new GalMapRegions.ManualCorrections("Eurus", y: -3000 ),
                    new GalMapRegions.ManualCorrections("The Perseus Transit", x: -3000, y: -3000 ),
                    new GalMapRegions.ManualCorrections("Zephyrus", x: 0, y: 2000 ),
                };

                edsmgalmapregions = new GalMapRegions();
                edsmgalmapregions.CreateObjects("edsmregions", items, rObjects, edsmmapping, 8000, corr: corr);
            }

            if ((parts & Parts.Regions) != 0)
            {
                elitemapregions = new GalMapRegions();
                elitemapregions.CreateObjects("eliteregions", items, rObjects, eliteregions, 8000);
                EliteRegionsEnable = false;
            }

            if ((parts & Parts.StarDots) != 0)
            {
                int gran = 8;
                Bitmap heat = galaxybitmap.Function(galaxybitmap.Width / gran, galaxybitmap.Height / gran, mode: GLOFC.Utils.BitMapHelpers.BitmapFunction.HeatMap);
                //heat.Save(@"c:\code\heatmap.jpg", System.Drawing.Imaging.ImageFormat.Jpeg);

                Random rnd = new Random(23);

                GLBuffer buf = items.NewBuffer(16 * 350000, false);     // since RND is fixed, should get the same number every time.
                buf.StartWrite(0); // get a ptr to the whole schebang

                int xcw = (right - left) / heat.Width;
                int zch = (back - front) / heat.Height;

                int points = 0;

                for (int x = 0; x < heat.Width; x++)
                {
                    for (int z = 0; z < heat.Height; z++)
                    {
                        int i = heat.GetPixel(x, z).R;
                        if (i > 32)
                        {
                            int gx = left + x * xcw;
                            int gz = front + z * zch;

                            float dx = (float)Math.Abs(gx) / 45000;
                            float dz = (float)Math.Abs(25889 - gz) / 45000;
                            double d = Math.Sqrt(dx * dx + dz * dz);     // 0 - 0.1412
                            d = 1 - d;  // 1 = centre, 0 = unit circle
                            d = d * 2 - 1;  // -1 to +1
                            double dist = ObjectExtensionsNumbersBool.GaussianDist(d, 1, 1.4);

                            int c = Math.Min(Math.Max(i * i * i / 120000, 1), 40);
                            //int c = Math.Min(Math.Max(i * i * i / 24000000, 1), 40);

                            dist *= 2000 / lyscale;
                            //System.Diagnostics.Debug.WriteLine("{0} {1} : dist {2} c {3}", x, z, dist, c);
                            //System.Diagnostics.Debug.Write(c);
                            GLPointsFactory.RandomStars4(buf, c, gx, gx + xcw, gz, gz + zch, (int)dist, (int)-dist, rnd, w: 0.8f);
                            points += c;
                            System.Diagnostics.Debug.Assert(points < buf.Length / 16);
                        }
                    }
                    //System.Diagnostics.Debug.WriteLine(".");
                }

                buf.StopReadWrite();

                stardots = new GalaxyStarDots();
                items.Add(stardots);
                GLRenderState rc = GLRenderState.Points(1);
                rc.DepthTest = false; // note, if this is true, there is a wierd different between left and right in view.. not sure why
                rObjects.Add(stardots, "stardots", GLRenderableItem.CreateVector4(items, OpenTK.Graphics.OpenGL4.PrimitiveType.Points, rc, buf, points));
                System.Diagnostics.Debug.WriteLine("Stars " + points);
            }

            rObjects.Add(new GLOperationClearDepthBuffer()); // clear depth buffer and now use full depth testing on the rest

            if ((parts & Parts.StarDots) != 0)
            {
                Bitmap starflare = BaseUtils.Icons.IconSet.GetBitmap("GalMap.StarFlare2");
                items.Add(new GLTexture2D(starflare, SizedInternalFormat.Rgba8), "lensflare");
                starsprites = new GLPointSpriteShader(items.Tex("lensflare"), 64, 40);
                items.Add(starsprites, "PS");
                var p = GLPointsFactory.RandomStars4(1000, 0, 25899 / lyscale, 10000 / lyscale, 1000 / lyscale, -1000 / lyscale);
                GLRenderState rps = GLRenderState.PointsByProgram();
                rObjects.Add(starsprites, "starsprites", GLRenderableItem.CreateVector4Color4(items, OpenTK.Graphics.OpenGL4.PrimitiveType.Points, rps, p, 
                                                new Color4[] { Color.White }));
            }

            if ((parts & Parts.Grid) != 0)
            {
                var gridvertshader = new DynamicGridVertexShader(Color.Cyan);
                var gridfragshader = new GLPLFragmentShaderVSColor();
                gridshader = new GLShaderPipeline(gridvertshader, gridfragshader);
                items.Add(gridshader, "DYNGRID");

                GLRenderState rl = GLRenderState.Lines();
                gridrenderable = GLRenderableItem.CreateNullVertex(OpenTK.Graphics.OpenGL4.PrimitiveType.Lines, rl, drawcount: 2);
                rObjects.Add(gridshader, "DYNGRIDRENDER", gridrenderable);
            }

            if ((parts & Parts.Grid) != 0)
            {
                var gridtextlabelsvertshader = new DynamicGridCoordVertexShader();
                var gridtextfragshader = new GLPLFragmentShaderTexture2DIndexed(0);
                gridtextshader = new GLShaderPipeline(gridtextlabelsvertshader, gridtextfragshader);
                items.Add(gridtextshader, "DYNGRIDBitmap");

                GLRenderState rl = GLRenderState.Tri(cullface: false);
                GLTexture2DArray gridtexcoords = new GLTexture2DArray();
                items.Add(gridtexcoords, "PLGridBitmapTextures");
                rObjects.Add(gridtextshader, "DYNGRIDBitmapRENDER", GLRenderableItem.CreateNullVertex(OpenTK.Graphics.OpenGL4.PrimitiveType.TriangleStrip, rl, drawcount: 4, instancecount: 9));
            }

            GLStorageBlock findresults = items.NewStorageBlock(findblock);

            var starimagearray = new GLTexture2DArray();
            Bitmap[] starcroppedbitmaps = BodyToImages.StarBitmaps(new RectangleF(16, 16, 68, 68));     // we own these
            if ( !EliteDangerousCore.DB.SystemsDatabase.Instance.HasStarType )
            {
                starcroppedbitmaps[(int)EDStar.Unknown] = starcroppedbitmaps[(int)EDStar.G];        // no star info, use G type.
            }
            // debug output of what is shown..
            //for (int b = 0; b < starcroppedbitmaps.Length; b++) starcroppedbitmaps[b].Save(@"c:\code\" + $"star{b}_{Enum.GetName(typeof(EDStar),b)}.bmp", System.Drawing.Imaging.ImageFormat.Png);

            // textures are all the same size, based on texture[0]. Make sure its the biggest
            starimagearray.CreateLoadBitmaps(starcroppedbitmaps, SizedInternalFormat.Rgba8, ownbmp: true, texturesize: starcroppedbitmaps[(int)EDStar.O].Size, alignment: ContentAlignment.MiddleCenter);
            items.Add(starimagearray);

            // control word for each star type. Note we have the ability to cull by star type by setting the control word to -1. bit 0-15 = image, bit 16 = no sunspots.
            // See LPLFragmentShaderTexture2DWSelectorSunspot (subspot and texture select) and GLPLVertexShaderModelWorldTextureAutoScale (culling)
            long[] starimagearraycontrolword = new long[starcroppedbitmaps.Length];
            for (uint i = 0; i < starcroppedbitmaps.Length; i++)
                starimagearraycontrolword[i] = i;
            starimagearraycontrolword[(int)EDStar.N] |= 65536;          // don't show sunspots for these
            starimagearraycontrolword[(int)EDStar.H] |= 65536;
            starimagearraycontrolword[(int)EDStar.X] |= 65536;
            starimagearraycontrolword[(int)EDStar.Nebula] |= 65536;
            starimagearraycontrolword[(int)EDStar.StellarRemnantNebula] |= 65536;
            starimagearraycontrolword[(int)EDStar.SuperMassiveBlackHole] |= 65536;

            if ((parts & Parts.GalObjects) != 0)
            {
                galmapobjects = new GalMapObjects();
                galmapobjects.CreateObjects(items, rObjects, edsmmapping, findresults, true);
            }

            float galaxysunsize = 0.4f;
            float travelsunsize = 0.5f;        // slightly bigger hides the double painting
            float labelw = galaxysunsize * 10;
            float labelh = galaxysunsize * 10 / 6;
            float labelhoff = -(Math.Max(travelsunsize,galaxysunsize)/2.0f + labelh / 2.0f);
            Size labelbitmapsize = new Size(160, 16);
            Font labelfont = new Font("Arial", 8.25f);
            float tapesize = 0.25f;

            if ((parts & Parts.TravelPath) != 0)
            {
                travelpath = new TravelPath();
                travelpath.TextBitMapSize = labelbitmapsize;
                travelpath.LabelSize = new Vector3(labelw, 0, labelh);
                travelpath.LabelOffset = new Vector3(0, labelhoff, 0);
                travelpath.Font = labelfont;
                travelpath.Start("TP", 200000, travelsunsize, tapesize, findresults, new Tuple<GLTexture2DArray, long[]>(starimagearray, starimagearraycontrolword), true, items, rObjects);
                travelpath.CreatePath(parent.DiscoveryForm.History, galmapobjects?.PositionsWithEnable);
                travelpath.SetSystem(parent.DiscoveryForm.History.LastSystem);
            }

            if ((parts & Parts.NavRoute) != 0)
            {
                navroute = new TravelPath();
                navroute.TextBitMapSize = labelbitmapsize;
                navroute.LabelSize = new Vector3(labelw, 0, labelh);
                navroute.LabelOffset = new Vector3(0, labelhoff, 0);
                navroute.Font = labelfont;
                navroute.Start("NavRoute", 10000, travelsunsize, tapesize, findresults, new Tuple<GLTexture2DArray, long[]>(starimagearray, starimagearraycontrolword), true, items, rObjects);
                UpdateNavRoute();
            }

            if ((parts & Parts.Bookmarks) != 0)
            {
                bookmarks = new Bookmarks();
                bookmarks.Start(items, rObjects, 1.0f, findresults, true);
                UpdateBookmarks();
            }

            if ((parts & Parts.GalaxyStars) != 0)
            {
                galaxystars = new GalaxyStars();

                if ((parts & Parts.PrepopulateGalaxyStarsLocalArea) != 0)        
                {
                    galaxystars.SectorSize = 20;
                    //    galaxystars.ShowDistance = true;// decided show distance is a bad idea, but we keep the code in case i change my mind
                    //    galaxystars.BitMapSize = new Size(galaxystars.BitMapSize.Width, galaxystars.BitMapSize.Height * 2);     // more v height for ly text
                }

                galaxystars.TextBitMapSize = labelbitmapsize;
                galaxystars.LabelSize = new Vector3(labelw, 0, labelh );
                galaxystars.LabelOffset = new Vector3(0,labelhoff,0);
                galaxystars.Font = labelfont;

                galaxystars.Create(items, rObjects, new Tuple<GLTexture2DArray, long[]>(starimagearray, starimagearraycontrolword), galaxysunsize, findresults, galmapobjects);
            }

            if ((parts & Parts.Route) != 0)
            {
                routepath = new TravelPath();       // we have no data here, it needs a push to display this, so we just create
                routepath.TextBitMapSize = labelbitmapsize;
                routepath.LabelSize = new Vector3(labelw, 0, labelh);
                routepath.LabelOffset = new Vector3(0, labelhoff, 0);
                routepath.Font = labelfont;

                routepath.Start("Route", 10000, travelsunsize, tapesize, findresults, new Tuple<GLTexture2DArray, long[]>(starimagearray, starimagearraycontrolword), true, items, rObjects);
            }

            System.Diagnostics.Debug.Assert(glwfc.IsContextCurrent());

            // Matrix calc holding transform info

            matrixcalc = new GLMatrixCalc();
            matrixcalc.PerspectiveNearZDistance = 0.5f;
            matrixcalc.PerspectiveFarZDistance = 120000f / lyscale;
            matrixcalc.InPerspectiveMode = true;
            matrixcalc.ResizeViewPort(this, glwfc.Size);          // must establish size before starting

            // menu system

            displaycontrol = new GLControlDisplay(items, glwfc, matrixcalc, true, 0.00001f, 0.00001f);       // hook form to the window - its the master
            displaycontrol.Font = new Font("Arial", 10f);
            displaycontrol.Focusable = true;          // we want to be able to focus and receive key presses.
            displaycontrol.SetFocus();

            displaycontrol.Paint += (ts) => {
                System.Diagnostics.Debug.Assert(displaycontrol.IsContextCurrent());
                // MCUB set up by Controller3DDraw which did the work first
                galaxymenu?.UpdateCoords(gl3dcontroller);
                displaycontrol.Animate(glwfc.ElapsedTimems);

                GLStatics.ClearDepthBuffer();         // clear the depth buffer, so we are on top of all previous renders.
                displaycontrol.Render(glwfc.RenderState, ts);
            };


            GLBaseControl.Themer = MapThemer.Theme;

            // 3d controller

            gl3dcontroller = new Controller3D();
            gl3dcontroller.PosCamera.ZoomMax = 5000;   
            gl3dcontroller.ZoomDistance = 3000F / lyscale;
            gl3dcontroller.PosCamera.ZoomMin = 0.1f;
            gl3dcontroller.PosCamera.ZoomScaling = 1.1f;
            gl3dcontroller.YHoldMovement = true;
            gl3dcontroller.PaintObjects = Controller3DDraw;
            gl3dcontroller.KeyboardTravelSpeed = (ms, eyedist) =>
            {
                double eyedistr = Math.Pow(eyedist, 1.0);
                float v = (float)Math.Max(eyedistr / 2000, 0);
                // System.Diagnostics.Debug.WriteLine("Speed " + eyedistr + " "+ v);
                return (float)ms * v;
            };

            // start hooks the glwfc paint function up, first, so it gets to go first
            // No ui events from glwfc.
            gl3dcontroller.Start(matrixcalc, glwfc, new Vector3(0, 0, 0), new Vector3(140.75f, 0, 0), 0.5F, registermouseui: false, registerkeyui: false);
            gl3dcontroller.Hook(displaycontrol, glwfc); // we get 3dcontroller events from displaycontrol, so it will get them when everything else is unselected
            displaycontrol.Hook();  // now we hook up display control to glwin, and paint

            displaycontrol.MouseClick += MouseClickOnMap;       // grab mouse UI

            if ((parts & Parts.Menu) != 0)
            {
                galaxymenu = new MapMenu(this, parts, userimages);
            }

            if ((parts & Parts.RightClick) != 0)
            {
                rightclickmenu = new GLContextMenu("RightClickMenu",true,
                    new GLMenuItemLabel("RCMTitle", "Context Menu"),
                    new GLMenuItemSeparator("RCMSepar", 5,5, ContentAlignment.MiddleCenter),
                    new GLMenuItem("RCMInfo", "Information")
                    {
                        MouseClick = (s, e) =>
                        {
                            if (e.Button == GLMouseEventArgs.MouseButtons.Left)
                            {
                                var nl = NameLocationDescription(rightclickmenu.Tag, parent.DiscoveryForm.History.GetLast);
                                var bkm = rightclickmenu.Tag as EliteDangerousCore.DB.BookmarkClass;

                                System.Diagnostics.Debug.WriteLine($"Info {nl.Item1} {nl.Item2}");

                                GLFormConfigurable cfg = new GLFormConfigurable("Info");
                                cfg.Tag = "SolidBackground";
                                GLMultiLineTextBox tb = new GLMultiLineTextBox("MLT", new Rectangle(10, 10, 1000, 1000), nl.Item3);
                                tb.Font = cfg.Font = displaycontrol.Font;                             // set the font up first, as its needed for config
                                var sizer = tb.CalculateTextArea(new Size(50, 24), new Size(displaycontrol.Width - 64, displaycontrol.Height - 64));
                                tb.Size = sizer.Item1;
                                tb.EnableVerticalScrollBar = true;
                                tb.EnableHorizontalScrollBar = sizer.Item2;
                                tb.CursorToEnd();
                                tb.BackColor = cfg.BackColor;
                                tb.ReadOnly = true;
                                tb.SetSelection(0, 0);
                                cfg.AddOK("OK");            // order important for tab control
                                cfg.AddButton("goto", "Goto", new Point(0, 0), anchor: AnchorType.AutoPlacement);
                                if (bkm != null)
                                    cfg.AddButton("edit", "Edit", new Point(0, 0), anchor: AnchorType.AutoPlacement);
                                cfg.Add("tb", tb);
                                cfg.Init(e.ViewportLocation, nl.Item1);
                                cfg.InstallStandardTriggers();
                                cfg.Trigger += (form, entry, name, tag) =>
                                {
                                    if (name == "goto")
                                    {
                                        gl3dcontroller.SlewToPositionZoom(nl.Item2, 300, -1);
                                    }
                                    else if (name == "edit")
                                    {
                                        cfg.Close();
                                        EditBookmark(bkm);
                                    }
                                };
                                cfg.ResumeLayout();
                                displaycontrol.Add(cfg);
                                cfg.Moveable = true;
                            }
                        }
                    },
                    new GLMenuItem("RCMEditBookmark", "Edit Bookmark")
                    {
                        Click = (s1) =>
                        {
                            rightclickmenu.Visible = false;     // because its a winform dialog we are showing, the menu won't shut down during show
                                                                // so we set this to invisible (not close, won't work inside here)
                            var bkm = rightclickmenu.Tag as EliteDangerousCore.DB.BookmarkClass;

                            if (bkm == null)
                            {
                                var nl = NameLocationDescription(rightclickmenu.Tag, parent.DiscoveryForm.History.GetLast);
                                bkm = EliteDangerousCore.DB.GlobalBookMarkList.Instance.FindBookmarkOnSystem(nl.Item1);
                            }

                            var res = BookmarkHelpers.ShowBookmarkForm(parent.DiscoveryForm, parent.DiscoveryForm, null, bkm);
                            if (res)
                                UpdateBookmarks();

                        }
                    },
                    new GLMenuItem("RCMZoomIn", "Goto Zoom In")
                    {
                        Click = (s1) =>
                        {
                            var nl = NameLocationDescription(rightclickmenu.Tag, parent.DiscoveryForm.History.GetLast);
                            gl3dcontroller.SlewToPositionZoom(nl.Item2, 300, -1);
                        }
                    },
                    new GLMenuItem("RCMGoto", "Goto Position")
                    {
                        Click = (s1) =>
                        {
                            var nl = NameLocationDescription(rightclickmenu.Tag, parent.DiscoveryForm.History.GetLast);
                            System.Diagnostics.Debug.WriteLine($"Goto {nl.Item1} {nl.Item2}");
                            gl3dcontroller.SlewToPosition(nl.Item2, -1);
                        }
                    },
                    new GLMenuItem("RCMLookAt", "Look At")
                    {
                        Click = (s1) =>
                        {
                            var nl = NameLocationDescription(rightclickmenu.Tag, null);
                            gl3dcontroller.PanTo(nl.Item2, -1);
                        }
                    },
                    new GLMenuItem("RCMViewStarDisplay", "Display system")
                    {
                        Click = (s1) =>
                        {
                            ISystem s = rightclickmenu.Tag is HistoryEntry ? ((HistoryEntry)rightclickmenu.Tag).System : (ISystem)rightclickmenu.Tag;
                            ScanDisplayForm.ShowScanOrMarketForm(parent.FindForm(), s,  parent.DiscoveryForm.History, 0.8f, System.Drawing.Color.Purple);
                        }
                    },
                    new GLMenuItem("RCMViewSpansh", "View on Spansh")
                    {
                        Click = (s1) =>
                        {
                            ISystem s = rightclickmenu.Tag is HistoryEntry ? ((HistoryEntry)rightclickmenu.Tag).System : (ISystem)rightclickmenu.Tag;
                            EliteDangerousCore.Spansh.SpanshClass.LaunchBrowserForSystem(s.SystemAddress.Value);
                        }
                    },
                    new GLMenuItem("RCMViewEDSM", "View on EDSM")
                    {
                        Click = (s1) =>
                        {
                            ISystem s = rightclickmenu.Tag is HistoryEntry ? ((HistoryEntry)rightclickmenu.Tag).System : (ISystem)rightclickmenu.Tag;

                            EDSMClass edsm = new EDSMClass();
                            if (!edsm.ShowSystemInEDSM(s.Name))
                                ExtendedControls.MessageBoxTheme.Show(parent.FindForm(), "System could not be found - has not been synched or EDSM is unavailable");
                        }
                    },
                    new GLMenuItem("RCMNewBookmark", "New Bookmark")
                    {
                        Click = (s1) =>
                        {
                            rightclickmenu.Visible = false;     // see above for this reason
                            var nl = NameLocationDescription(rightclickmenu.Tag, parent.DiscoveryForm.History.GetLast);

                            var res = BookmarkHelpers.ShowBookmarkForm(parent.DiscoveryForm, parent.DiscoveryForm,
                                                    new SystemClass(nl.Item1, null, nl.Item2.X, nl.Item2.Y, nl.Item2.Z), null);

                            if ( res )      // if changed
                            {
                                UpdateBookmarks();
                            }
                        }
                    },
                    new GLMenuItem("RCMDeleteBookmark", "Delete Bookmark")
                    {
                        Click = (s1) =>
                        {
                            var bkm = rightclickmenu.Tag as EliteDangerousCore.DB.BookmarkClass;
                            if (bkm == null)
                            {
                                var nl2 = NameLocationDescription(rightclickmenu.Tag, parent.DiscoveryForm.History.GetLast);
                                bkm = EliteDangerousCore.DB.GlobalBookMarkList.Instance.FindBookmarkOnSystem(nl2.Item1);
                            }

                            if ( bkm != null )
                                DeleteBookmark(bkm);
                        }
                    },
                    new GLMenuItem("RCMAddExpedition", "Add to expedition")
                    {
                        Click = (s1) =>
                        {
                            ISystem s = rightclickmenu.Tag is HistoryEntry ? ((HistoryEntry)rightclickmenu.Tag).System : (ISystem)rightclickmenu.Tag;
                            AddSystemsToExpedition?.Invoke(new List<string>() { s.Name });      // use call back to pass back up
                        }
                    }
                );

                rightclickmenu.Opening += (ms,opentag) =>
                {
                    System.Diagnostics.Debug.WriteLine("Right click opening");

                    rightclickmenu.Size = new Size(10, 10);     // reset to small, and let the autosizer work again as we are going to change content

                    var nl = NameLocationDescription(rightclickmenu.Tag, parent.DiscoveryForm.History.GetLast);
                    ((GLMenuItemLabel)ms["RCMTitle"]).Text = nl.Item1 + " " + nl.Item2.ToString();

                    ISystem s = rightclickmenu.Tag is HistoryEntry ? ((HistoryEntry)rightclickmenu.Tag).System : rightclickmenu.Tag is ISystem ? (ISystem)rightclickmenu.Tag : null;

                    ms["RCMAddExpedition"].Visible = ms["RCMViewStarDisplay"].Visible = ms["RCMViewEDSM"].Visible = s != null;

                    if (s!=null)
                    {
                        System.Diagnostics.Debug.WriteLine("Right click on system " + nl.Item1);
                        var bkm = EliteDangerousCore.DB.GlobalBookMarkList.Instance.FindBookmarkOnSystem(nl.Item1);

                        ms["RCMDeleteBookmark"].Visible = ms["RCMEditBookmark"].Visible = bkm != null;
                        ms["RCMNewBookmark"].Visible = bkm == null;
                        ms["RCMViewSpansh"].Visible = s.SystemAddress != null;
                    }
                    else
                    {
                        ms["RCMNewBookmark"].Visible = false;
                        ms["RCMEditBookmark"].Visible = ms["RCMDeleteBookmark"].Visible = rightclickmenu.Tag is EliteDangerousCore.DB.BookmarkClass;
                    }
                };
            }

            // Autocomplete text box at top for searching

            GLTextBoxAutoComplete tbac = galaxymenu?.EntryTextBox;
            if (tbac != null)
            {
                tbac.PerformAutoCompleteInThread = (s, obj, set) =>       // in the autocomplete thread, so EDSM lookup
                {
                    System.Diagnostics.Debug.WriteLine($"Autocomplete look up EDSM systems on {s}");
                    EliteDangerousCore.DB.SystemCache.ReturnSystemAutoCompleteList(s, obj, set);      // perform the system cache autocomplete

                    foreach( var gmo in edsmmapping.GalacticMapObjects)
                    {
                        foreach( var name in gmo.Names)
                        {
                            if (s.Length < 3 ? name.StartsWith(s, StringComparison.InvariantCultureIgnoreCase) : name.Contains(s, StringComparison.InvariantCultureIgnoreCase))
                                set.Add(name);
                        }
                    }
                };

                tbac.PerformAutoCompleteInUIThread = (s, obj, set) =>    // complete autocomplete in UI thread, for parts declared in UI, run second
                {
                    System.Diagnostics.Debug.Assert(Application.MessageLoop);
                    System.Diagnostics.Debug.WriteLine($"Autocomplete look up GMO/Travel systems on {s}");

                    List<string> list = new List<string>();
                    if (travelpath?.CurrentListSystem != null)
                        list.AddRange(travelpath.CurrentListSystem.Where(x => s.Length < 3 ? x.Name.StartsWith(s, StringComparison.InvariantCultureIgnoreCase) : x.Name.Contains(s, StringComparison.InvariantCultureIgnoreCase)).Select(x => x.Name));
                    if (routepath?.CurrentListSystem != null)
                        list.AddRange(routepath.CurrentListSystem.Where(x => s.Length < 3 ? x.Name.StartsWith(s, StringComparison.InvariantCultureIgnoreCase) : x.Name.Contains(s, StringComparison.InvariantCultureIgnoreCase)).Select(x => x.Name));
                    if (navroute?.CurrentListSystem != null)
                        list.AddRange(navroute.CurrentListSystem.Where(x => s.Length < 3 ? x.Name.StartsWith(s, StringComparison.InvariantCultureIgnoreCase) : x.Name.Contains(s, StringComparison.InvariantCultureIgnoreCase)).Select(x => x.Name));

                    foreach (var x in list)
                        set.Add(x);
                };

                tbac.SelectedEntry = (a) =>     // in UI thread
                {
                    System.Diagnostics.Debug.Assert(Application.MessageLoop);       // must be in UI thread
                    System.Diagnostics.Debug.WriteLine("Selected " + tbac.Text);
                    var gmo = edsmmapping?.GalacticMapObjects.Find(x => x.IsNameWildCard(tbac.Text,true));

                    Vector3? pos = null;
                    if (gmo != null)
                    {
                        System.Diagnostics.Debug.WriteLine("Move to gmo " + gmo.Points[0]);
                        pos = new Vector3((float)gmo.Points[0].X, (float)gmo.Points[0].Y, (float)gmo.Points[0].Z);
                    }
                    else
                    {
                        var isys = travelpath?.CurrentListSystem?.Find(x => x.Name.Equals(tbac.Text, StringComparison.InvariantCultureIgnoreCase));
                        if (isys == null)
                            isys = routepath?.CurrentListSystem?.Find(x => x.Name.Equals(tbac.Text, StringComparison.InvariantCultureIgnoreCase));
                        if (isys == null)
                            isys = navroute?.CurrentListSystem?.Find(x => x.Name.Equals(tbac.Text, StringComparison.InvariantCultureIgnoreCase));
                        if (isys == null)
                            isys = EliteDangerousCore.DB.SystemCache.FindSystem(tbac.Text);     // final chance, the system DB

                        if (isys != null)
                        {
                            System.Diagnostics.Debug.WriteLine("Move to sys " + isys.Name);
                            pos = new Vector3((float)isys.X, (float)isys.Y, (float)isys.Z);
                        }
                        else
                            tbac.InErrorCondition = true;
                    }

                    if ( pos != null )
                    {
                        gl3dcontroller.SlewToPosition(pos.Value, -1);

                        if ((parts & Parts.PrepopulateGalaxyStarsLocalArea) != 0)
                        {
                            galaxystars.Request9x3Box(pos.Value);
                        }
                    }
                };
            }

            if (galaxystars != null)
            {
                galaxystars.Start();
            }

            System.Diagnostics.Debug.Assert(glwfc.IsContextCurrent());

            string shaderlog = GLShaderLog.ShaderLog;
            if (shaderlog.HasChars())
            {
                var inf = new ExtendedControls.InfoForm();
                inf.Info("Shader log - report to EDD team", Properties.Resources.edlogo_3mo_icon, shaderlog);
                inf.Show();
            }

            mapcreatedokay = GLShaderLog.Okay;      // record shader status
            return mapcreatedokay;
        }
        #endregion

        #region Helpers

        public void UpdateTravelPath()   // new history entry
        {
            if (travelpath != null )
            {
                travelpath.CreatePath(parent.DiscoveryForm.History, galmapobjects?.PositionsWithEnable);
                travelpath.SetSystem(parent.DiscoveryForm.History.LastSystem);
            }

            CheckRefreshLocalArea();  // also see if local stars need updating
        }

        public void UpdateNavRoute()
        {
            if (navroute != null )
            {
                var route = parent.DiscoveryForm.History.GetLastHistoryEntry(x => x.EntryType == JournalTypeEnum.NavRoute)?.journalEntry as EliteDangerousCore.JournalEvents.JournalNavRoute;
                if (route?.Route != null) // If a navroute with a valid route..
                {
                    var syslist = route.Route.Select(x => new SystemClass(x.StarSystem, null, x.StarPos.X, x.StarPos.Y, x.StarPos.Z, SystemSource.FromJournal, x.EDStarClass)).Cast<ISystem>().ToList();
                    navroute.CreatePath(syslist, Color.Purple, galmapobjects?.PositionsWithEnable);
                }
            }
        }

        public void UpdateRoutePath()
        {
            if ( routepath != null )
            {
                var curroute = routepath.CurrentListSystem != null ? routepath.CurrentListSystem : new List<ISystem>();
                routepath.CreatePath(curroute, Color.Green, galmapobjects?.PositionsWithEnable);
            }
        }

        public void UpdateDueToGMOEnableChanging()       // feed in list of galmapobject positions to other classes so they don't repeat
        {
            if (galmapobjects != null)
            {
                if (galaxystars != null)
                {
                    galaxystars.Clear();
                }
                if (travelpath != null)
                {
                    UpdateTravelPath();
                }
                if (navroute != null)
                {
                    UpdateNavRoute();
                }
                if (routepath != null)
                {
                    UpdateRoutePath();
                }
            }
        }

        public void UpdateEDSMStarsLocalArea()
        {
            if (galaxystars != null )
            {
                galaxystars.ClearBoxAround();
                CheckRefreshLocalArea();
            }
        }

        public void CheckRefreshLocalArea()
        {
            HistoryEntry he = parent.DiscoveryForm.History.GetLast;       // may be null
            // basic check we are operating in this mode
            if (galaxystars != null && he != null && he.System.HasCoordinate && (parts & Parts.PrepopulateGalaxyStarsLocalArea) != 0 )
            {
                var hepos = new Vector3((float)he.System.X, (float)he.System.Y, (float)he.System.Z);

                if ((galaxystars.CurrentPos - hepos).Length >= LocalAreaSize / 4)       // if current pos too far away, go for it
                {
                    galaxystars.ClearBoxAround();
                    galaxystars.RequestBoxAround(hepos, LocalAreaSize);      // this sets CurrentPos
                }
            }
        }
        public void AddMoreStarsAtLookat()
        {
            galaxystars.Request9x3Box(gl3dcontroller.PosCamera.LookAt);
        }

        public void UpdateBookmarks()
        {
            if (bookmarks != null)
            {
                var bks = EliteDangerousCore.DB.GlobalBookMarkList.Instance.Bookmarks;
                var list = bks.Select(a => new Vector4((float)a.x, (float)a.y + 1.5f, (float)a.z, 1)).ToArray();
                bookmarks.Create(list);
                FillBookmarkForm();
            }
        }

        public void EditBookmark(EliteDangerousCore.DB.BookmarkClass bkm)
        {
            var res = BookmarkHelpers.ShowBookmarkForm(parent.DiscoveryForm, parent.DiscoveryForm, null, bkm);
            if (res)
                UpdateBookmarks();
        }
        public void DeleteBookmark(EliteDangerousCore.DB.BookmarkClass bkm)
        {
            GLMessageBox msg = new GLMessageBox("Confirm", displaycontrol, new Point(int.MinValue, 0), "Confirm Deletion of bookmark" + " " + bkm.Name, "Warning", 
                            GLMessageBox.MessageBoxButtons.OKCancel, 
                            callback: (mbox,dr) =>
                            {
                                if (dr == GLForm.DialogResultEnum.OK)
                                {
                                    EliteDangerousCore.DB.GlobalBookMarkList.Instance.Delete(bkm);
                                    UpdateBookmarks();
                                }
                            }
                        );
        }

        GLForm bmform = null;

        public void ToggleBookmarkList(bool on)
        {
            System.Diagnostics.Debug.WriteLine($"Bookmark list {on}");

            if ( bmform == null )
            {
                bmform = new GLForm("BKForm", "Bookmarks", new Rectangle(5, 40, Math.Min(400,displaycontrol.Width-30), Math.Min(600,displaycontrol.Height-60)));
                GLDataGridView dgv = null;
                dgv = new GLDataGridView("BKDGV", new Rectangle(10, 10, 10, 10));
                dgv.Dock = DockingType.Fill;
                dgv.SelectCellSelectsRow = true;
                dgv.AllowUserToSelectMultipleRows = false;
                dgv.ColumnFillMode = GLDataGridView.ColFillMode.FillWidth;
                dgv.HorizontalScrollVisible = false;
                dgv.SelectRowOnRightClick = true;
                dgv.RowHeaderEnable = false;
                var col0 = dgv.CreateColumn(fillwidth: 100, title: "Star");
                var col1 = dgv.CreateColumn(fillwidth: 50, title: "X");
                col1.SortCompare = GLDataGridViewSorts.SortCompareNumeric;
                var col2 = dgv.CreateColumn(fillwidth: 50, title: "Y");
                col2.SortCompare = GLDataGridViewSorts.SortCompareNumeric;
                var col3 = dgv.CreateColumn(fillwidth: 50, title: "Z");
                col3.SortCompare = GLDataGridViewSorts.SortCompareNumeric;
                var col4 = dgv.CreateColumn(fillwidth: 100, title: "Note");
                dgv.AddColumn(col0);
                dgv.AddColumn(col1);
                dgv.AddColumn(col2);
                dgv.AddColumn(col3);
                dgv.AddColumn(col4);

                dgv.MouseClickOnGrid += (row, col, mouseevent) =>       // intercept mouse click on grid rather than row selection since we can see what button clicked it
                {
                    if (mouseevent.Button == GLMouseEventArgs.MouseButtons.Left && row >= 0)
                    {
                        var bk = dgv.Rows[row].Tag as EliteDangerousCore.DB.BookmarkClass;
                        gl3dcontroller.SlewToPosition(new Vector3((float)bk.x, (float)bk.y, (float)bk.z), -1);
                    }
                };

                dgv.ContextMenuGrid = new GLContextMenu("BookmarksRightClickMenu", true,
                    new GLMenuItem("BKEdit", "Edit")
                    {
                        MouseClick = (s, e) =>
                        {
                            var pos = dgv.ContextMenuGrid.Tag as GLDataGridView.RowColPos;
                            System.Diagnostics.Debug.WriteLine($"Click on {pos.Row}");
                            if (pos.Row >= 0)
                            {
                                var bk = dgv.Rows[pos.Row].Tag as EliteDangerousCore.DB.BookmarkClass;
                                EditBookmark(bk);
                            }
                        }
                    },
                    new GLMenuItem("BKNew", "New")
                    {
                        MouseClick = (s, e) =>
                        {
                            var res = BookmarkHelpers.ShowBookmarkForm(parent.DiscoveryForm, parent.DiscoveryForm, null, null);
                            if (res)
                                UpdateBookmarks();
                        }
                    },
                    new GLMenuItem("BKDelete", "Delete")
                    {
                        MouseClick = (s, e) =>
                        {
                            var pos = dgv.ContextMenuGrid.Tag as GLDataGridView.RowColPos;
                            System.Diagnostics.Debug.WriteLine($"Click on {pos.Row}");
                            if (pos.Row >= 0)
                            {
                                var bk = dgv.Rows[pos.Row].Tag as EliteDangerousCore.DB.BookmarkClass;
                                DeleteBookmark(bk);
                            }
                        }
                    });

                dgv.ContextMenuGrid.Opening = (cms,tag) => {
                    var rcp = tag as GLDataGridView.RowColPos;      
                    cms["BKEdit"].Visible = cms["BKDelete"].Visible = rcp.Row >= 0;
                    cms.Tag = rcp; // transfer the opening position tag to the cms for the menu items, so it can get it
                };


                bmform.Add(dgv);
                bmform.FormClosed += (f) => { bmform = null; displaycontrol.ApplyToControlOfName("MSTPBookmarks", (c) => { ((GLCheckBox)c).CheckedNoChangeEvent = false; }); };


                FillBookmarkForm();

                displaycontrol.Add(bmform);
            }

            bmform.Visible = on;
        }

        public void GoToTravelSystem(int dir)      //0 = current, 1 = next, -1 = prev
        {
            var isys = dir == 0 ? travelpath.CurrentSystem : (dir < 0 ? travelpath.PrevSystem() : travelpath.NextSystem());
            if (isys != null)
            {
                gl3dcontroller.SlewToPosition(new Vector3((float)isys.X, (float)isys.Y, (float)isys.Z), -1);
                SetEntryText(isys.Name);
            }
        }

        public void GoToCurrentSystem(float lydist = 50f)
        {
            HistoryEntry he = parent.DiscoveryForm.History.GetLast;       // may be null
            if (he != null)
            {
                GoToSystem(he.System, lydist);
                travelpath?.SetSystem(he.System.Name);
            }
        }

        public void GoToSystem(ISystem isys, float lydist = 50f)
        {
            gl3dcontroller.SlewToPositionZoom(new Vector3((float)isys.X, (float)isys.Y, (float)isys.Z), gl3dcontroller.ZoomDistance / lydist, -1);
            SetEntryText(isys.Name);
        }

        public void ViewGalaxy()
        {
            gl3dcontroller.PosCamera.CameraRotation = 0;
            gl3dcontroller.PosCamera.GoToZoomPan(new Vector3(0, 0, 0), new Vector2(140.75f, 0), 0.5f, 3);
        }

        public void SetRoute(List<ISystem> syslist)
        {
            if (routepath != null)
                routepath.CreatePath(syslist, Color.Green, galmapobjects?.PositionsWithEnable);
        }

        private void SetEntryText(string text)
        {
            if (galaxymenu.EntryTextBox != null)
            {
                galaxymenu.EntryTextBox.Text = text;
                galaxymenu.EntryTextBox.CancelAutoComplete();
            }
            displaycontrol.SetFocus();
        }

        #endregion

        #region Helpers

        private void FillBookmarkForm()
        {
            if (bmform != null)
            {
                var dgv = bmform.ControlsZ[0] as GLDataGridView;
                dgv.Clear();

                var gl = EliteDangerousCore.DB.GlobalBookMarkList.Instance.Bookmarks;

                foreach (var bk in gl)
                {
                    var row = dgv.CreateRow();

                    row.AddCell(new GLDataGridViewCellText(bk.Name),
                                new GLDataGridViewCellText(bk.x.ToString("N1")), new GLDataGridViewCellText(bk.y.ToString("N1")), new GLDataGridViewCellText(bk.z.ToString("N1")),
                                new GLDataGridViewCellText(bk.Note));
                    row.Tag = bk;
                    row.AutoSize = true;
                    dgv.AddRow(row);
                }
            }
        }

        #endregion

        #region Enables

        public bool GalaxyDisplay { get { return galaxyshader?.Enable ?? true; } set { if (galaxyshader != null) galaxyshader.Enable = value; glwfc.Invalidate(); } }
        public bool StarDotsSpritesDisplay { get { return stardots?.Enable ?? true; } set { if (stardots != null) stardots.Enable = starsprites.Enable = value; glwfc.Invalidate(); } }
        public int GalaxyStars { get { return galaxystars?.EnableMode ?? 0; } set { if (galaxystars != null) galaxystars.EnableMode = value; glwfc.Invalidate(); } }
        public int GalaxyStarsMaxObjects { get { return galaxystars?.MaxObjectsAllowed ?? 100000; } set { if (galaxystars != null ) galaxystars.MaxObjectsAllowed = value; } } 
        public bool Grid { get { return gridshader?.Enable ?? true; } set { if (gridshader != null) gridshader.Enable = gridtextshader.Enable = value; glwfc.Invalidate(); } }

        public bool NavRouteDisplay { get { return navroute?.EnableTape ?? true; } set { if (navroute != null) navroute.EnableTape = navroute.EnableStars = navroute.EnableText = value; glwfc.Invalidate(); } }
        public bool TravelPathTapeDisplay { get { return travelpath?.EnableTape ?? true; } set { if (travelpath != null) travelpath.EnableTape = value; glwfc.Invalidate(); } }
        public bool TravelPathTextDisplay { get { return travelpath?.EnableText ?? true; } set { if (travelpath != null) travelpath.EnableText = value; glwfc.Invalidate(); } }
        public void TravelPathRefresh() { if (travelpath != null) UpdateTravelPath(); }   // travelpath.Refresh() manually after these have changed
        public DateTime TravelPathStartDateUTC { get { return travelpath?.TravelPathStartDateUTC ?? new DateTime(2014,12,14); } set { if (travelpath != null && travelpath.TravelPathStartDateUTC != value) { travelpath.TravelPathStartDateUTC = value; } } }
        public bool TravelPathStartDateEnable { get { return travelpath?.TravelPathStartDateEnable ?? true; } set { if (travelpath != null && travelpath.TravelPathStartDateEnable != value) { travelpath.TravelPathStartDateEnable = value; } } }
        public DateTime TravelPathEndDateUTC { get { return travelpath?.TravelPathEndDateUTC ?? new DateTime(2040,1,1); } set { if (travelpath != null && travelpath.TravelPathEndDateUTC != value) { travelpath.TravelPathEndDateUTC = value; } } }
        public bool TravelPathEndDateEnable { get { return travelpath?.TravelPathEndDateEnable ?? true; } set { if (travelpath != null && travelpath.TravelPathEndDateEnable != value) { travelpath.TravelPathEndDateEnable = value; } } }

        public bool UserImagesEnable { get { return usertexturebitmaps?.Enable ?? false; } set { if (usertexturebitmaps != null) { usertexturebitmaps.Enable = value; glwfc.Invalidate(); } } }

        public bool GalObjectDisplay
        {
            get { return galmapobjects?.Enable ?? true; }
            set
            {
                if (galmapobjects != null)
                {
                    galmapobjects.SetShaderEnable(value);
                    UpdateDueToGMOEnableChanging();
                    glwfc.Invalidate();
                }
            }
        }
        public void SetGalObjectTypeEnable(string id, bool state) { 
            if (galmapobjects != null) 
            { 
                galmapobjects.SetGalObjectTypeEnable(id, state);
                UpdateDueToGMOEnableChanging();
                glwfc.Invalidate(); 
            } 
        }
        public void SetAllGalObjectTypeEnables(string set) { 
            if (galmapobjects != null) 
            { 
                galmapobjects.SetAllEnables(set);
                UpdateDueToGMOEnableChanging();
                glwfc.Invalidate(); 
            } 
        }
        public bool GetGalObjectTypeEnable(string id) { return galmapobjects?.GetGalObjectTypeEnable(id) ?? true; }
        public string GetAllGalObjectTypeEnables() { return galmapobjects?.GetAllEnables() ?? ""; }
        public bool EDSMRegionsEnable { get { return edsmgalmapregions?.Enable ?? false; } set { if (edsmgalmapregions != null) edsmgalmapregions.Enable = value; glwfc.Invalidate(); } }
        public bool EDSMRegionsOutlineEnable { get { return edsmgalmapregions?.Outlines ?? true; } set { if (edsmgalmapregions != null) edsmgalmapregions.Outlines = value; glwfc.Invalidate(); } }
        public bool EDSMRegionsShadingEnable { get { return edsmgalmapregions?.Regions ?? false; } set { if (edsmgalmapregions != null) edsmgalmapregions.Regions = value; glwfc.Invalidate(); } }
        public bool EDSMRegionsTextEnable { get { return edsmgalmapregions?.Text ?? true; } set { if (edsmgalmapregions != null) edsmgalmapregions.Text = value; glwfc.Invalidate(); } }
        public bool EliteRegionsEnable { get { return elitemapregions?.Enable ?? true; } set { if (elitemapregions != null) elitemapregions.Enable = value; glwfc.Invalidate(); } }
        public bool EliteRegionsOutlineEnable { get { return elitemapregions?.Outlines ?? true; } set { if (elitemapregions != null) elitemapregions.Outlines = value; glwfc.Invalidate(); } }
        public bool EliteRegionsShadingEnable { get { return elitemapregions?.Regions ?? false; } set { if (elitemapregions != null) elitemapregions.Regions = value; glwfc.Invalidate(); } }
        public bool EliteRegionsTextEnable { get { return elitemapregions?.Text ?? true; } set { if (elitemapregions != null) elitemapregions.Text = value; glwfc.Invalidate(); } }
        public bool ShowBookmarks { get { return bookmarks?.Enable ?? true; } set { if (bookmarks != null) bookmarks.Enable = value; glwfc.Invalidate(); } }
        public int LocalAreaSize { get { return localareasize; } set { if ( value != localareasize ) { localareasize = value; UpdateEDSMStarsLocalArea(); } } }

        public int AutoScaleGMOs
        {
            get { return autoscalegmo; }
            set
            {
                autoscalegmo = value;
                if (galmapobjects != null)
                    galmapobjects.SetAutoScale(autoscalegmo);
            }
        }
        public int AutoScaleBookmarks
        {
            get { return autoscalebookmarks; }
            set
            {
                autoscalebookmarks = value;
                if (bookmarks != null)
                    bookmarks.SetAutoScale(autoscalebookmarks);
            }
        }
        public int AutoScaleGalaxyStars
        {
            get { return autoscalegalstars; }
            set
            {
                autoscalegalstars = value;
                if (galaxystars != null)
                    galaxystars.SetAutoScale(autoscalegalstars);
            }
        }
        #endregion

        #region State load

        public void LoadState(MapSaver defaults, bool restorepos, int loadlimit)
        {
            gl3dcontroller.ChangePerspectiveMode(defaults.GetSetting("GAL3DMode", true));

            GalaxyDisplay = defaults.GetSetting("GD", true);
            StarDotsSpritesDisplay = defaults.GetSetting("SDD", true);
            NavRouteDisplay = defaults.GetSetting("NRD", true);
            TravelPathTapeDisplay = defaults.GetSetting("TPD", true);
            TravelPathTextDisplay = defaults.GetSetting("TPText", true);
            TravelPathStartDateUTC = defaults.GetSetting("TPSD", new DateTime(2014, 12, 16));
            TravelPathStartDateEnable = defaults.GetSetting("TPSDE", false);
            TravelPathEndDateUTC = defaults.GetSetting("TPED", DateTime.UtcNow.AddMonths(1));
            TravelPathEndDateEnable = defaults.GetSetting("TPEDE", false);
            if ((TravelPathStartDateEnable || TravelPathEndDateEnable) && travelpath != null)
                UpdateTravelPath();

            GalObjectDisplay = defaults.GetSetting("GALOD", true);
            SetAllGalObjectTypeEnables(defaults.GetSetting("GALOBJLIST", ""));

            UserImagesEnable = defaults.GetSetting("ImagesEnable", true);
            userimages?.LoadFromString(defaults.GetSetting("ImagesList", ""));

            EDSMRegionsEnable = defaults.GetSetting("ERe", false);
            EDSMRegionsOutlineEnable = defaults.GetSetting("ERoe", false);
            EDSMRegionsShadingEnable = defaults.GetSetting("ERse", false);
            EDSMRegionsTextEnable = defaults.GetSetting("ERte", false);

            EliteRegionsEnable = defaults.GetSetting("ELe", true);
            EliteRegionsOutlineEnable = defaults.GetSetting("ELoe", true);
            EliteRegionsShadingEnable = defaults.GetSetting("ELse", false);
            EliteRegionsTextEnable = defaults.GetSetting("ELte", true);

            Grid = defaults.GetSetting("GRIDS", true);

            GalaxyStars = defaults.GetSetting("GALSTARS", 3);
            GalaxyStarsMaxObjects = (loadlimit == 0) ? defaults.GetSetting("GALSTARSOBJ", 500000) : loadlimit;
            LocalAreaSize = defaults.GetSetting("LOCALAREALY", 50);

            ShowBookmarks = defaults.GetSetting("BKMK", true);

            AutoScaleBookmarks = defaults.GetSetting("AUTOSCALEBookmarks", 30);
            AutoScaleGMOs = defaults.GetSetting("AUTOSCALEGMO", 30);
            AutoScaleGalaxyStars = defaults.GetSetting("AUTOSCALEGS", 4);

            if (restorepos)
            {
                var pos = defaults.GetSetting("POSCAMERA", "");
                if (pos.HasChars() && !gl3dcontroller.SetPositionCamera(pos))     // go thru gl3dcontroller to set default position, so we reset the model matrix
                    System.Diagnostics.Trace.WriteLine($"*** NOTE 3DMAPS {pos} did not decode");
            }
        }

        public void SaveState(MapSaver defaults)
        {
            if (!mapcreatedokay)
                return;

            defaults.PutSetting("GAL3DMode", gl3dcontroller.MatrixCalc.InPerspectiveMode);
            defaults.PutSetting("GD", GalaxyDisplay);
            defaults.PutSetting("SDD", StarDotsSpritesDisplay);
            defaults.PutSetting("TPD", TravelPathTapeDisplay);
            defaults.PutSetting("TPText", TravelPathTextDisplay);
            defaults.PutSetting("NRD", NavRouteDisplay);
            defaults.PutSetting("TPSD", TravelPathStartDateUTC);
            defaults.PutSetting("TPSDE", TravelPathStartDateEnable);
            defaults.PutSetting("TPED", TravelPathEndDateUTC);
            defaults.PutSetting("TPEDE", TravelPathEndDateEnable);
            defaults.PutSetting("GALOD", GalObjectDisplay);
            defaults.PutSetting("GALOBJLIST", GetAllGalObjectTypeEnables());
            defaults.PutSetting("ERe", EDSMRegionsEnable);
            defaults.PutSetting("ERoe", EDSMRegionsOutlineEnable);
            defaults.PutSetting("ERse", EDSMRegionsShadingEnable);
            defaults.PutSetting("ERte", EDSMRegionsTextEnable);
            defaults.PutSetting("ELe", EliteRegionsEnable);
            defaults.PutSetting("ELoe", EliteRegionsOutlineEnable);
            defaults.PutSetting("ELse", EliteRegionsShadingEnable);
            defaults.PutSetting("ELte", EliteRegionsTextEnable);
            defaults.PutSetting("GRIDS", Grid);
            defaults.PutSetting("GALSTARS", GalaxyStars);
            defaults.PutSetting("GALSTARSOBJ", GalaxyStarsMaxObjects);
            defaults.PutSetting("LOCALAREALY", LocalAreaSize);
            var pos = gl3dcontroller.PosCamera.StringPositionCamera;
            defaults.PutSetting("POSCAMERA", pos);
            defaults.PutSetting("BKMK", bookmarks?.Enable ?? true);
            defaults.PutSetting("AUTOSCALEBookmarks", AutoScaleBookmarks);
            defaults.PutSetting("AUTOSCALEGMO", AutoScaleGMOs);
            defaults.PutSetting("AUTOSCALEGS", AutoScaleGalaxyStars);

            defaults.PutSetting("ImagesEnable", UserImagesEnable);
            if (userimages != null)
                defaults.PutSetting("ImagesList", userimages.ImageStringList());
        }

        #endregion

        #region Images

        public void LoadImages()
        {
            if (userimages != null)
            {
                usertexturebitmaps.Clear();
                userimages.LoadBitmaps(
                    null,   // resources
                    (ie)=>  // text
                    {
                        BaseUtils.StringParser sp = new BaseUtils.StringParser(ie.ImagePathOrURL);
                        
                        // "Text with escape",font,size,colorfore,colorback,format,bitmapwidth,bitmapheight.
                        // quote words if they have spaces
                        // format is centre,right,left,top,bottom,middle,nowrap

                        string text = sp.NextQuotedWordComma(replaceescape: true);

                        if ( text != null )
                        {
                            string fontname = sp.NextQuotedWordComma() ?? "Arial";     // all may be null  
                            double size = sp.NextDoubleComma(",") ?? 10;
                            string forecolour = sp.NextQuotedWordComma() ?? "White";
                            string backcolour = sp.NextQuotedWordComma() ?? "Transparent";
                            string format = sp.NextQuotedWordComma() ?? "centre middle";
                            int bitmapwidth = sp.NextIntComma(",") ?? 1024;
                            int bitmapheight = sp.NextInt(",") ?? 256;

                            Font fnt = new Font(fontname, (float)size);
                            Color fore = Color.FromName(forecolour);
                            Color back = Color.FromName(backcolour);
                            StringFormat fmt = format.StringFormatFromName();

                            usertexturebitmaps.Add(null, null, text, fnt,fore, back, new Size(bitmapwidth, bitmapheight),
                                new Vector3(ie.Centre.X, ie.Centre.Y, ie.Centre.Z),
                                new Vector3(ie.Size.X, 0, ie.Size.Y),
                                new Vector3(ie.RotationDegrees.X.Radians(), ie.RotationDegrees.Y.Radians(), ie.RotationDegrees.Z.Radians()),
                                fmt, 1,
                                ie.RotateToViewer, ie.RotateElevation, ie.AlphaFadeScalar, ie.AlphaFadePosition);
                            fnt.Dispose();
                            fmt.Dispose();
                        }

                    },
                    (ie) => // http load
                    {
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            string name = ie.ImagePathOrURL;
                            string filename = ie.ImagePathOrURL.Replace("http://", "", StringComparison.InvariantCultureIgnoreCase).
                                        Replace("https://", "", StringComparison.InvariantCultureIgnoreCase).SafeFileString();
                            string path = System.IO.Path.Combine(EDDOptions.Instance.DownloadedImages(), filename);
                            System.Diagnostics.Debug.WriteLine($"HTTP load {name} to {path}");

                            bool res = BaseUtils.DownloadFile.HTTPDownloadFile(name, path, false, out bool newfile);

                            if (res)
                            {
                                parent.DiscoveryForm.BeginInvoke((MethodInvoker)delegate 
                                {
                                    if (ie.LoadBitmap(path))
                                    {
                                        usertexturebitmaps.Add(null, null, ie.Bitmap, 1,
                                            new Vector3(ie.Centre.X, ie.Centre.Y, ie.Centre.Z),
                                            new Vector3(ie.Size.X, 0, ie.Size.Y),
                                            new Vector3(ie.RotationDegrees.X.Radians(), ie.RotationDegrees.Y.Radians(), ie.RotationDegrees.Z.Radians()),
                                            ie.RotateToViewer, ie.RotateElevation, ie.AlphaFadeScalar, ie.AlphaFadePosition);
                                    }
                                });
                            }
                        });
                    },
                    (ie)=> //file
                    {
                        usertexturebitmaps.Add(null, null, ie.Bitmap, 1,
                            new Vector3(ie.Centre.X, ie.Centre.Y, ie.Centre.Z),
                            new Vector3(ie.Size.X, 0, ie.Size.Y),
                            new Vector3(ie.RotationDegrees.X.Radians(), ie.RotationDegrees.Y.Radians(), ie.RotationDegrees.Z.Radians()),
                            ie.RotateToViewer, ie.RotateElevation, ie.AlphaFadeScalar, ie.AlphaFadePosition);
                    },
                    (ie)=>  // image:
                    {
                        string name = ie.ImagePathOrURL.Substring(6).Trim();
                        if ( BaseUtils.Icons.IconSet.Instance.Contains(name))
                        {
                            ie.Bitmap = (Bitmap)BaseUtils.Icons.IconSet.Instance.Get(name);
                            ie.OwnBitmap = false;
                            usertexturebitmaps.Add(null, null, ie.Bitmap, 1,
                                new Vector3(ie.Centre.X, ie.Centre.Y, ie.Centre.Z),
                                new Vector3(ie.Size.X, 0, ie.Size.Y),
                                new Vector3(ie.RotationDegrees.X.Radians(), ie.RotationDegrees.Y.Radians(), ie.RotationDegrees.Z.Radians()),
                                ie.RotateToViewer, ie.RotateElevation, ie.AlphaFadeScalar, ie.AlphaFadePosition);
                        }
                    }
                    );

            }
        }

        #endregion

        #region Finding

        private Object FindObjectOnMap(Point loc)
        {
            // fixed debug loc = new Point(896, 344);

            float hez = float.MaxValue, gmoz = float.MaxValue, galstarz = float.MaxValue, routez = float.MaxValue, navz = float.MaxValue, bkmz = float.MaxValue;

            GalacticMapObject gmo = galmapobjects?.FindPOI(loc, glwfc.RenderState, matrixcalc.ViewPort.Size, out gmoz);      //z are maxvalue if not found
            int? bkm = bookmarks?.Find(loc, glwfc.RenderState, matrixcalc.ViewPort.Size, out bkmz);
            HistoryEntry he = travelpath?.FindSystem(loc, glwfc.RenderState, matrixcalc.ViewPort.Size, out hez) as HistoryEntry;
            ISystem galstar = galaxystars?.Find(loc, glwfc.RenderState, matrixcalc.ViewPort.Size, out galstarz);
            SystemClass route = routepath?.FindSystem(loc, glwfc.RenderState, matrixcalc.ViewPort.Size, out routez) as SystemClass;
            SystemClass nav = navroute?.FindSystem(loc, glwfc.RenderState, matrixcalc.ViewPort.Size, out navz) as SystemClass;

            if (gmo != null && gmoz < bkmz && gmoz < hez && gmoz < galstarz && gmoz < routez && gmoz < navz)      // got gmo, and closer than the others
            {
                System.Diagnostics.Debug.WriteLine($"Find GMO {gmo.NameList}");
                return gmo;
            }

            if (bkm != null && bkmz < hez && bkmz < galstarz && bkmz < routez && bkmz < navz)
            {
                var bks = EliteDangerousCore.DB.GlobalBookMarkList.Instance.Bookmarks;
                System.Diagnostics.Debug.WriteLine($"Find Bookmark {bks[bkm.Value].Name}");
                return bks[bkm.Value];
            }

            if (galstar != null)        // if its set, find it in the db to give more info on position etc
            {
                ISystem s = EliteDangerousCore.DB.SystemCache.FindSystem(galstar.Name); // look up by name
                if (s != null)                          // must normally be found, so return
                    galstar = s;
                else
                    galstar = null;
            }

            // if he set, and less than galstar, or galstar is set and its name is the same as he, use he.  this gives preference to he's over galstar
            if (he != null && (hez < galstarz || (galstar!=null && he.System.Name == galstar.Name)))
            {
                System.Diagnostics.Debug.WriteLine($"Find HE {he.System.Name}");
                return he;
            }

            if (galstar != null && galstarz < navz && galstarz < routez)
            {
                System.Diagnostics.Debug.WriteLine($"Find Galstar {galstar.Name}");
                return galstar;
            }

            if (route != null && routez < navz )
            {
                System.Diagnostics.Debug.WriteLine($"Find Route {route.Name}");
                return route;
            }

            if (nav != null)
            {
                System.Diagnostics.Debug.WriteLine($"Find NavRoute {nav.Name}");
                return nav;
            }

            return null;
        }

        // from obj, return info about it, its name, location, and description
        // give current he for information purposes
        private Tuple<string, Vector3, string> NameLocationDescription(Object obj, HistoryEntry curpos)       
        {
            var he = obj as HistoryEntry;
            var gmo = obj as GalacticMapObject;
            var bkm = obj as EliteDangerousCore.DB.BookmarkClass;
            var sys = obj as ISystem;

            if (sys != null)
                System.Diagnostics.Debug.WriteLine($"3dmap Lookup ISystem {sys.Name} edsmid {sys.EDSMID} sa {sys.SystemAddress}");

            string name = he != null ? he.System.Name : gmo != null ? gmo.NameList : bkm != null ? bkm.Name : sys.Name;
            if (bkm != null)
                name = "Bookmark " + name;

            Vector3 pos = he != null ? new Vector3((float)he.System.X, (float)he.System.Y, (float)he.System.Z) :
                            gmo != null ? new Vector3((float)gmo.Points[0].X, (float)gmo.Points[0].Y, (float)gmo.Points[0].Z) :
                                bkm != null ? new Vector3((float)bkm.x, (float)bkm.y, (float)bkm.z) :
                                    new Vector3((float)sys.X, (float)sys.Y, (float)sys.Z);

            string info = "";

            if (curpos != null)
            {
                double dist = curpos.System.Distance(pos.X, pos.Y, pos.Z);
                if ( dist>0)
                    info += $"Distance {dist:N1} ly";
            }

            if (he != null && he.System.MainStarType != EDStar.Unknown)
                info = info.AppendPrePad($"Star Type {Bodies.StarName(he.System.MainStarType)}",Environment.NewLine);

            if (sys != null && sys.MainStarType != EDStar.Unknown)
                info = info.AppendPrePad($"Star Type {Bodies.StarName(sys.MainStarType)}",Environment.NewLine);

            info = info.AppendPrePad($"Position {pos.X:0.#}, {pos.Y:0.#}, {pos.Z:0.#}" + Environment.NewLine, Environment.NewLine);

            if (he != null)
            {
                info = info.AppendPrePad("Visited on:",Environment.NewLine);

                foreach (var e in travelpath.CurrentListHE)
                {
                    if (e.System.Name.Equals(he.System.Name))
                    {
                        var t = EDDConfig.Instance.ConvertTimeToSelectedFromUTC(e.EventTimeUTC);
                        info = info.AppendPrePad($"{t.ToString()}", Environment.NewLine);
                    }
                }
            }
            else if (gmo != null)
            {
                info = info.AppendPrePad(gmo.Description,Environment.NewLine);
            }
            else if ( bkm != null)
            {
                info = info.AppendPrePad(bkm.Note, Environment.NewLine);
            }

            return new Tuple<string, Vector3, string>(name, pos, info);
        }

        #endregion

        #region Display

        // Context is set.
        public void Systick()
        {
            System.Diagnostics.Debug.Assert(glwfc.IsContextCurrent());

            gl3dcontroller.HandleKeyboardSlews(true, OtherKeys);
            gl3dcontroller.RecalcMatrixIfMoved();
            glwfc.Invalidate();
        }

        int lastgridwidth = 100;

        // Context is set.
        private void Controller3DDraw(Controller3D c3d, ulong time)
        {
            if (!mapcreatedokay)
                return;

            System.Diagnostics.Debug.Assert(glwfc.IsContextCurrent());

            GLMatrixCalcUniformBlock mcb = ((GLMatrixCalcUniformBlock)items.UB("MCUB"));
            mcb.SetFull(gl3dcontroller.MatrixCalc);        // set the matrix unform block to the controller 3d matrix calc.

            // set up the grid shader size

            if (gridshader != null && gridshader.Enable)
            {
                // set the dynamic grid properties

                var vertshader = gridshader.GetShader<DynamicGridVertexShader>(ShaderType.VertexShader);

                gridrenderable.InstanceCount = vertshader.ComputeGridSize(gl3dcontroller.MatrixCalc.LookAt.Y, gl3dcontroller.MatrixCalc.EyeDistance, out lastgridwidth);

                vertshader.SetUniforms(gl3dcontroller.MatrixCalc.LookAt, lastgridwidth, gridrenderable.InstanceCount);

                // set the coords fader

                float coordfade = lastgridwidth == 10000 ? (0.7f - (c3d.MatrixCalc.EyeDistance / 20000).Clamp(0.0f, 0.7f)) : 0.7f;
                Color coordscol = Color.FromArgb(coordfade < 0.05 ? 0 : 150, Color.Cyan);
                var tvshader = gridtextshader.GetShader<DynamicGridCoordVertexShader>(ShaderType.VertexShader);
                tvshader.ComputeUniforms(lastgridwidth, gl3dcontroller.MatrixCalc, gl3dcontroller.PosCamera.CameraDirection, coordscol, Color.Transparent);
            }

            //// set the galaxy volumetric block

            if (galaxyrenderable != null && galaxyshader.Enable)
            {
                galaxyrenderable.InstanceCount = volumetricblock.Set(gl3dcontroller.MatrixCalc, volumetricboundingbox, gl3dcontroller.MatrixCalc.InPerspectiveMode ? 50.0f : 0);        // set up the volumentric uniform
                //System.Diagnostics.Debug.WriteLine("GI {0}", galaxyrendererable.InstanceCount);
                galaxyshader.SetFader(gl3dcontroller.MatrixCalc.EyePosition, gl3dcontroller.MatrixCalc.EyeDistance, gl3dcontroller.MatrixCalc.InPerspectiveMode);
            }

            if (travelpath != null)
                travelpath.Update(time, gl3dcontroller.MatrixCalc.EyeDistance);
            if (routepath != null)
                routepath.Update(time, gl3dcontroller.MatrixCalc.EyeDistance);
            if (navroute != null)
                navroute.Update(time, gl3dcontroller.MatrixCalc.EyeDistance);

            if (galmapobjects != null && galmapobjects.Enable)
                galmapobjects.Update(time, gl3dcontroller.MatrixCalc.EyeDistance);

            if (galaxystars != null)
            {
                if (galaxystars.EnableMode > 0)      // enable mode must be on to show something
                {
                    // if auto update, and eyedistance close, and db is okay, try it

                    if ((parts & Parts.AutoEDSMStarsUpdate) != 0 && gl3dcontroller.MatrixCalc.EyeDistance < 400 &&
                                    EliteDangerousCore.DB.SystemsDatabase.Instance.RebuildRunning == false)
                    {
                        galaxystars.Request9x3BoxConditional(gl3dcontroller.PosCamera.LookAt);
                    }

                    galaxystars.Update(time, gl3dcontroller.MatrixCalc.EyeDistance);
                }

                if ( galaxymenu != null )
                    galaxymenu.DBStatus.Visible = galaxystars.DBActive;     // always indicate DB Active status

            }

            rObjects.Render(glwfc.RenderState, gl3dcontroller.MatrixCalc, verbose: false);

            GL.Flush(); // ensure everything is in the grapghics pipeline
        }

        #endregion

        #region UI

        private void MouseClickOnMap(GLBaseControl s, GLMouseEventArgs e)
        {
            int distmovedsq = gl3dcontroller.MouseMovedSq(e);        //3dcontroller is monitoring mouse movements
            if (distmovedsq < 4)
            {
                //  System.Diagnostics.Debug.WriteLine("map click");
                Object item = FindObjectOnMap(e.ViewportLocation);

                if (item != null)
                {
                    if (e.Button == GLMouseEventArgs.MouseButtons.Left)
                    {
                        if (item is HistoryEntry)
                            travelpath.SetSystem((item as HistoryEntry).System);
                        var nl = NameLocationDescription(item, null);
                        System.Diagnostics.Debug.WriteLine("Click on and slew to " + nl.Item1);
                        SetEntryText(nl.Item1);
                        gl3dcontroller.SlewToPosition(nl.Item2, -1);
                    }
                    else if (e.Button == GLMouseEventArgs.MouseButtons.Right)
                    {
                        if (rightclickmenu != null)
                        {
                            rightclickmenu.Tag = item;
                            rightclickmenu.Show(displaycontrol, new Point(e.Location.X,e.Location.Y-32));
                        }
                    }
                }
            }
        }

        private void OtherKeys(GLOFC.Controller.KeyboardMonitor kb)
        {
            // See OFC PositionCamera for keys used
            // F keys are reserved for KeyPresses action pack

            if ((parts & Parts.PerspectiveChange) != 0 && kb.HasBeenPressed(Keys.P, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                gl3dcontroller.ChangePerspectiveMode(!gl3dcontroller.MatrixCalc.InPerspectiveMode);
            }

            if ((parts & Parts.YHoldButton) != 0 && kb.HasBeenPressed(Keys.O, KeyboardMonitor.ShiftState.None))
            {
                gl3dcontroller.YHoldMovement = !gl3dcontroller.YHoldMovement;
            }

            if (kb.HasBeenPressed(Keys.D1, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                GalaxyDisplay = !GalaxyDisplay;
            }
            if (kb.HasBeenPressed(Keys.D2, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                Grid = !Grid;
            }
            if (kb.HasBeenPressed(Keys.D3, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                StarDotsSpritesDisplay = !StarDotsSpritesDisplay;
            }
            if (kb.HasBeenPressed(Keys.D4, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                TravelPathTapeDisplay = !TravelPathTapeDisplay;
            }
            if (kb.HasBeenPressed(Keys.D5, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                GalObjectDisplay = !GalObjectDisplay;
            }
            if (kb.HasBeenPressed(Keys.D6, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                GalaxyStars = GalaxyStars >= 0 ? 0 : 3;
            }
            if (kb.HasBeenPressed(Keys.D7, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                if (EDSMRegionsEnable)
                    EDSMRegionsOutlineEnable = !EDSMRegionsOutlineEnable;
                else
                    EliteRegionsOutlineEnable = !EliteRegionsOutlineEnable;
            }
            if (kb.HasBeenPressed(Keys.D8, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                if (EDSMRegionsEnable)
                    EDSMRegionsShadingEnable = !EDSMRegionsShadingEnable;
                else
                    EliteRegionsShadingEnable = !EliteRegionsShadingEnable;
            }
            if (kb.HasBeenPressed(Keys.D9, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                if (EDSMRegionsEnable)
                    EDSMRegionsTextEnable = !EDSMRegionsTextEnable;
                else
                    EliteRegionsTextEnable = !EliteRegionsTextEnable;
            }
        }

        #endregion


    }
}
