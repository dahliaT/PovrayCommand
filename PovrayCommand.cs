
//Copyright (c) 2015, Dahlia Trimble
//All rights reserved.

//Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

//1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.

//2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.

//3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.

//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using OpenMetaverse;
using OpenMetaverse.Imaging;
using OpenMetaverse.Rendering;
using OpenMetaverse.StructuredData;
using OpenMetaverse.Assets;
using PrimMesher;

namespace OpenMetaverse.TestClient
{
    public class dPovrayCommand : Command
    {
        string usage = "Usage: dpovray <file.pov>";
        private IRendering m_primMesher;
        private Dictionary<uint, Primitive> mRootPrims = null;
        private String mKnownTexturesCacheFile = "knownTextures.txt";

        class TextureInfo
        {
            UUID mId;
            Color4 mMeanColor;

            public TextureInfo()
            { }

            public TextureInfo(OSD osd)
            {
                if (osd is OSDMap)
                {
                    Id = ((OSDMap)osd)["id"].AsUUID();
                    MeanColor = ((OSDMap)osd)["meancolor"].AsColor4();
                }
            }

            public UUID Id
            {
                get { return mId; }
                set { mId = value; }
            }

            public Color4 MeanColor
            {
                get { return mMeanColor; }
                set { mMeanColor = value; }
            }

            public OSD GetOsd()
            {
                OSDMap map = new OSDMap();
                map["id"] = Id;
                map["meancolor"] = MeanColor;
                return map;
            }
        }

        private Dictionary<UUID, TextureInfo> mKnownTextures = new Dictionary<UUID, TextureInfo>();

        public dPovrayCommand(TestClient testClient)
        {
            Name = "dpovray";
            Description = "Exports sim contents to a povray file. " + usage;
            Category = CommandCategory.Objects;

            List<string> renderers = RenderingLoader.ListRenderers(".");
            if (renderers.Count > 0)
                m_primMesher = RenderingLoader.LoadRenderer(renderers[0]);
            else
                Logger.Log(Name + ": No prim mesher loaded, prim rendering will be disabled", Helpers.LogLevel.Info);
        }

        public override string Execute(string[] args, UUID fromAgentID)
        {
            // load known texture table
            OSD knownTexturesOsd = null;
            if (File.Exists(mKnownTexturesCacheFile))
            {
                knownTexturesOsd = OSDParser.DeserializeJson(
                    File.ReadAllText(mKnownTexturesCacheFile));

                if (knownTexturesOsd is OSDArray)
                {
                    foreach (OSD osd in (OSDArray)knownTexturesOsd)
                    {
                        TextureInfo ti = new TextureInfo(osd);
                        mKnownTextures[ti.Id] = ti;
                    }
                }
                knownTexturesOsd = null;
            }

            string fileName = "sim.pov";

            if (args.Length > 0)
                fileName = args[args.Length - 1];
            if (!fileName.EndsWith(".pov"))
                fileName += ".pov";

            Logger.Log("dpovray: fileName:" + fileName, Helpers.LogLevel.Debug);

            ulong regionHandle = Client.Network.CurrentSim.Handle;

            bool success = ProcessScene(regionHandle, fileName);

            // load known textures again in case another bot wrote some while we were busy
            // load known texture table
            if (File.Exists(mKnownTexturesCacheFile))
            {
                knownTexturesOsd = OSDParser.DeserializeJson(
                    File.ReadAllText(mKnownTexturesCacheFile));

                if (knownTexturesOsd is OSDArray)
                {
                    foreach (OSD osd in (OSDArray)knownTexturesOsd)
                    {
                        TextureInfo ti = new TextureInfo(osd);
                        mKnownTextures[ti.Id] = ti;
                    }
                }
            }
            // now save our known textuer cache
            OSDArray knownTexturesArr = new OSDArray();
            foreach (KeyValuePair<UUID, TextureInfo> kvp in mKnownTextures)
            {
                if (kvp.Value != null)
                    knownTexturesArr.Add(kvp.Value.GetOsd());
            }
            File.WriteAllText(mKnownTexturesCacheFile, OSDParser.SerializeJsonString(knownTexturesArr));

            if (success)
                return "exported sim to file: " + fileName;

            return "error exporting sim to file: " + fileName;
        }

        private bool ProcessScene(ulong regionHandle, string fileName)
        {
            Simulator sim = null;
            lock (this.Client.Network.Simulators)
                sim = this.Client.Network.Simulators.Find(s => s.Handle == regionHandle);
            if (sim == null)
                return false;

            string baseName = fileName.Substring(0, fileName.Length - 4);

            using (StreamWriter sw = new StreamWriter(fileName))
            {
                sw.WriteLine("#include \"colors.inc\"");

                bool overheadOrthoCam = true; // set true for maptile view

                sw.WriteLine("camera");
                sw.WriteLine("{");
                if (overheadOrthoCam)
                {
                    sw.WriteLine("orthographic angle 40");
                    sw.WriteLine("location <12.8, 35, 12.8>");
                    sw.WriteLine("look_at <12.8, 0, 12.8>");
                    sw.WriteLine("right x * image_width / image_height");
                }
                else
                {
                    sw.WriteLine("location <12.5, 12, -15>");
                    sw.WriteLine("look_at  <12.5, 5,  5>");
                }
                sw.WriteLine("}");

                //sw.WriteLine("light_source { <2, 14, -3> color White}");
                sw.WriteLine("light_source { <-9, 284, -8> color White}");

                dPovTerrain terrain = new dPovTerrain(Client);
                BuildPovTerrainMesh(terrain.HeightField, sw, baseName);

                mRootPrims = new Dictionary<uint, Primitive>();

                sim.ObjectsPrimitives.ForEach(delegate(Primitive prim)
                {
                    if (prim.ParentID == 0)
                        mRootPrims[prim.LocalID] = prim;


                    if (prim.Textures != null)
                    { // get all textures so mean color gets stored
                        if (prim.Textures.FaceTextures != null)
                        {
                            foreach (var tef in prim.Textures.FaceTextures)
                            {
                                if (tef != null && tef.TextureID != null)
                                {
                                    if (mKnownTextures.ContainsKey(tef.TextureID))
                                        continue;

                                    Image im = GetImage(Client, tef.TextureID);
                                    if (im == null)
                                        mKnownTextures[tef.TextureID] = null;
                                    else
                                        im.Dispose();
                                }
                            }
                        }

                        if (prim.Textures.DefaultTexture != null && !mKnownTextures.ContainsKey(prim.Textures.DefaultTexture.TextureID))
                        {
                            Image im = GetImage(Client, prim.Textures.DefaultTexture.TextureID);
                            if (im == null)
                                mKnownTextures[prim.Textures.DefaultTexture.TextureID] = null;
                            else
                                im.Dispose();
                        }
                    }
                });

                List<Primitive> prims = new List<Primitive>();

                sim.ObjectsPrimitives.ForEach(delegate(Primitive prim)
                {
                    if (prim.ParentID != 0)
                    {
                        if (mRootPrims.ContainsKey(prim.ParentID))
                        {
                            Primitive root = mRootPrims[prim.ParentID];
                            if (root.ParentID == 0)
                                prims.Add(prim);
                        }
                    }
                    else if (prim.ParentID == 0)
                        prims.Add(prim);
                });

                foreach (var prim in prims)
                    if (prim.PrimData.PCode == PCode.Prim)
                        sw.WriteLine(PovMesh(prim));

                mRootPrims = null;
                sw.Flush();
                sw.Close();
            }

            return true;
        }

        Matrix4 GetModelviewMatrix(Primitive prim)
        {
            Vector3 pos = prim.Position;
            Vector3 scale = prim.Scale;
            Quaternion rot = prim.Rotation;

            if (prim.ParentID != 0)
            {
                if (mRootPrims.ContainsKey(prim.ParentID))
                {
                    Primitive parent = mRootPrims[prim.ParentID];
                    rot = parent.Rotation * rot;
                    pos = parent.Position + pos * parent.Rotation;
                }
                else
                    Logger.Log(Name + ": root prim:" + prim.ParentID.ToString() + " not found", Helpers.LogLevel.Warning);
            }

            Matrix4 mat = Matrix4.CreateScale(scale);
            mat *= Matrix4.CreateFromQuaternion(rot);
            mat *= Matrix4.CreateTranslation(pos);
            return mat;
        }

        private String PovMesh(Primitive prim)
        {
            string s;

            FacetedMesh renderMesh = null;

            if (prim.Sculpt != null && prim.Sculpt.SculptTexture != UUID.Zero)
            {
                if (prim.Sculpt.Type == SculptType.Mesh)
                {
                    byte[] meshData = GetMesh(prim.Sculpt.SculptTexture);
                    if (meshData == null)
                        return string.Empty;

                    AssetMesh meshAsset = new AssetMesh(prim.Sculpt.SculptTexture, meshData);
                    FacetedMesh.TryDecodeFromAsset(prim, meshAsset, DetailLevel.Highest, out renderMesh);
                    meshAsset = null;
                }
                else // not a mesh, must be a sculptie
                {
                    Image sculpt = GetImage(Client, prim.Sculpt.SculptTexture);
                    if (sculpt == null)
                        return string.Empty;

                    renderMesh = m_primMesher.GenerateFacetedSculptMesh(prim, (Bitmap)sculpt, DetailLevel.Medium);
                    sculpt.Dispose();
                }
            }
            else
                renderMesh = m_primMesher.GenerateFacetedMesh(prim, DetailLevel.Highest);

            if (renderMesh == null)
                return string.Empty;

            Matrix4 mv = GetModelviewMatrix(prim);

            using (StringWriter sw = new StringWriter())
            {
                for (int i = 0; i < renderMesh.Faces.Count; i++)
                {
                    var face = renderMesh.Faces[i];
                    Primitive.TextureEntryFace tef = null;
                    try { tef = prim.Textures.GetFace((uint)i); }
                    catch (Exception) { continue; }

                    int numVerts = face.Vertices.Count;
                    int numIndices = face.Indices.Count;
                    if (numVerts == 0 || numIndices == 0 || tef == null)
                        continue;

                    sw.WriteLine("mesh2");
                    sw.WriteLine("{");

                    sw.WriteLine("vertex_vectors");
                    sw.WriteLine("{");

                    sw.WriteLine(numVerts.ToString());
                    for (int vi = 0; vi < numVerts; vi++)
                    {
                        Vector3 v = face.Vertices[vi].Position;
                        sw.WriteLine(PovVector3((v * mv) * 0.1f));
                    }
                    sw.WriteLine("}"); // vertex_vectors

                    sw.WriteLine("face_indices");
                    sw.WriteLine("{");

                    sw.WriteLine((numIndices / 3).ToString());

                    for (int ti = 0; ti < numIndices; ti += 3)
                        sw.WriteLine(string.Format("<{0},{1},{2}>", face.Indices[ti], face.Indices[ti + 1], face.Indices[ti + 2]));

                    sw.WriteLine("}"); // face_indices


                    // material

                    Color4 clr = tef.RGBA;
                    if (tef.TextureID != null && mKnownTextures.ContainsKey(tef.TextureID))
                    {
                        var texInfo = mKnownTextures[tef.TextureID];
                        if (texInfo != null)
                            clr *= texInfo.MeanColor;
                    }
                    sw.WriteLine("pigment {rgbf ");
                    sw.WriteLine(string.Format("<{0},{1},{2},{3}>", clr.R, clr.G, clr.B, 1.0f - clr.A));
                    sw.WriteLine("}");

                    //sw.WriteLine("pigment {rgb <1, 0.6, 0.6>}");

                    sw.WriteLine("}"); // mesh2
                }

                s = sw.ToString();
            }

            return s;
        }

        String PovVector3(Vector3 v)
        {
            if (float.IsInfinity(v.X) || float.IsNaN(v.X))
                v.X = 0;
            if (float.IsInfinity(v.Y) || float.IsNaN(v.Y))
                v.Y = 0;
            if (float.IsInfinity(v.Z) || float.IsNaN(v.Z))
                v.Z = 0;

            return string.Format("<{0},{1},{2}>", v.X, v.Z, v.Y);
        }


        Image GetImage(TestClient client, UUID id)
        {
            byte[] assetData = new byte[0];

            AutoResetEvent fetchDone = new AutoResetEvent(false);
            client.Assets.RequestImage(id, delegate(TextureRequestState state, OpenMetaverse.Assets.AssetTexture asset)
            {
                if ((state == TextureRequestState.Finished) && (asset != null))
                {
                    fetchDone.Set();
                    assetData = asset.AssetData;
                }
            });
            if (!fetchDone.WaitOne(30000, false))
                return null;

            ManagedImage managedImage = null;

            OpenJPEG.DecodeToImage(assetData, out managedImage);

            if (managedImage == null)
                return null;

            bool hasAlpha = false;
            ulong rSum = 0;
            ulong gSum = 0;
            ulong bSum = 0;
            ulong aSum = 0;
            uint numPix = (uint)managedImage.Red.Length;
            var red = managedImage.Red;
            var green = managedImage.Green;
            var blue = managedImage.Blue;

            if ((managedImage.Channels & ManagedImage.ImageChannels.Alpha) != 0)
            {

                var alpha = managedImage.Alpha;
                for (uint i = 0; i < numPix; i++)
                    aSum += alpha[i];

                managedImage.ConvertChannels(managedImage.Channels & ~ManagedImage.ImageChannels.Alpha);
                hasAlpha = true;
            }
            for (uint i = 0; i < numPix; i++)
            {
                rSum += red[i];
                gSum += green[i];
                bSum += blue[i];
            }
            rSum /= numPix;
            gSum /= numPix;
            bSum /= numPix;
            aSum /= numPix;

            Color4 meanColor = new Color4((byte)rSum, (byte)gSum, (byte)bSum, hasAlpha ? (byte)aSum : (byte)255);

            TextureInfo ti = new TextureInfo();
            ti.Id = id;
            ti.MeanColor = meanColor;
            mKnownTextures[id] = ti;

            Bitmap imgData = LoadTGAClass.LoadTGA(new MemoryStream(managedImage.ExportTGA()));

            return (Image)imgData;
        }

        byte[] GetMesh(UUID id)
        {
            AutoResetEvent gotMesh = new AutoResetEvent(false);
            byte[] assetData = null;
            Client.Assets.RequestMesh(id, delegate(bool success, OpenMetaverse.Assets.AssetMesh meshAsset)
            {
                if (success)
                {
                    assetData = meshAsset.AssetData;
                    gotMesh.Set();
                }
            });
            if (!gotMesh.WaitOne(30000, false))
                return null;

            return assetData;
        }

        void BuildPovTerrainMesh(double[,] heights, StreamWriter sw, string baseName)
        {
            Bitmap texture = TerrainTexture(heights);
            texture.Save(baseName + "Terrain.png", ImageFormat.Png);
            texture.Dispose();

            if (heights == null || texture == null)
            {
                Logger.Log(Name + ": BuildPovTerrainMesh(): null heightfield or terrain texture", Helpers.LogLevel.Warning);
                return;
            }

            sw.WriteLine("mesh2 // terrain");
            sw.WriteLine("{");

            sw.WriteLine("vertex_vectors");
            sw.WriteLine("{");

            uint width = 256;
            uint height = 256;

            uint numVerts = width * height;
            uint numTris = (width - 1) * (height - 1) * 2;
            uint numIndices = numTris * 3;

            sw.WriteLine(numVerts.ToString());

            for (uint y = 0; y < height; y++)
            {
                for (uint x = 0; x < width; x++)
                {
                    Vector3 v = new Vector3((float)x, (float)y, (float)heights[x, y]);
                    sw.WriteLine(PovVector3(v * 0.1f));
                }
            }
            sw.WriteLine("}"); // vertex_vectors

            sw.WriteLine("uv_vectors");
            sw.WriteLine("{");
            sw.WriteLine(numVerts.ToString());

            for (uint y = 0; y < height; y++)
            {
                for (uint x = 0; x < width; x++)
                {
                    sw.WriteLine(string.Format("<{0},{1}>", (float)x / 255, (float)y / 255));
                }
            }
            sw.WriteLine("}"); // uv_vectors

            sw.WriteLine("face_indices");
            sw.WriteLine("{");

            sw.WriteLine((numTris).ToString());

            for (uint y = 0; y < height - 1; y++)
            {
                for (uint x = 0; x < width - 1; x++)
                {
                    uint v = y * width + x;
                    sw.WriteLine(string.Format("<{0},{1},{2}>", v, v + 1, v + width));
                    sw.WriteLine(string.Format("<{0},{1},{2}>", v + width + 1, v + width, v + 1));
                }
            }

            sw.WriteLine("}"); // face_indices


            // material

            Color4 clr = new Color4(0.15f, 0.5f, 0.1f, 1.0f);
            sw.WriteLine("pigment {rgbf ");
            sw.WriteLine(string.Format("<{0},{1},{2},{3}>", clr.R, clr.G, clr.B, 1.0f - clr.A));
            sw.WriteLine("}");

            //sw.WriteLine("pigment {rgb <1, 0.6, 0.6>}");

            sw.WriteLine("} // terrain"); // mesh2
        }

        Bitmap TerrainTexture(double[,] heights)
        {
            Bitmap bm = null;

            Simulator sim = Client.Network.CurrentSim;

            Bitmap[] textures = new Bitmap[4];
            float[] startHeights = new float[4];
            float[] heightRanges = new float[4];

            textures[0] = (Bitmap)GetImage(Client, sim.TerrainDetail0);
            textures[1] = (Bitmap)GetImage(Client, sim.TerrainDetail1);
            textures[2] = (Bitmap)GetImage(Client, sim.TerrainDetail2);
            textures[3] = (Bitmap)GetImage(Client, sim.TerrainDetail3);

            startHeights[0] = sim.TerrainStartHeight00;
            startHeights[1] = sim.TerrainStartHeight01;
            startHeights[2] = sim.TerrainStartHeight10;
            startHeights[3] = sim.TerrainStartHeight11;

            heightRanges[0] = sim.TerrainHeightRange00;
            heightRanges[1] = sim.TerrainHeightRange01;
            heightRanges[2] = sim.TerrainHeightRange10;
            heightRanges[3] = sim.TerrainHeightRange11;

            Vector3d regionPos = new Vector3d(0, 0, 0);

            bm = TerrainSplat.Splat(heights, textures, startHeights, heightRanges, regionPos);

            return bm;
        }

    }





    class dPovTerrain
    {
        double[,] mDHeightMap = null;
        TestClient mClient;
        Simulator mSim;


        public dPovTerrain(TestClient Client)
        {
            mClient = Client;
            mSim = mClient.Network.CurrentSim;
        }

        public double[,] HeightField
        {
            get
            {
                if (mDHeightMap != null)
                    return mDHeightMap;

                mSim = mClient.Network.CurrentSim;
                var terrain = mSim.Terrain;
                if (terrain == null)
                    return null;

                Logger.Log("dPovTerrain: terrain found!", Helpers.LogLevel.Debug);

                int height = 256;
                int width = 256;
                double[,] dHeightMap = new double[width, height];
                double zSum = 0;

                for (int x = 0; x < 256; x++)
                {
                    for (int y = 0; y < 256; y++)
                    {
                        float z = 0;
                        int patchNr = ((int)x / 16) * 16 + (int)y / 16;
                        if (terrain[patchNr] != null
                            && terrain[patchNr].Data != null)
                        {
                            float[] data = terrain[patchNr].Data;
                            z = data[(int)x % 16 * 16 + (int)y % 16];
                        }
                        dHeightMap[y, x] = z;
                        zSum += z;
                    }
                }

                return mDHeightMap = dHeightMap;
            }
        }

        void BuildTexture()
        {

        }



    }
}


