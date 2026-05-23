// MwmCollisionExtractor — extracts collision shapes from .hkt / .mwm files
//
// Build (Linux, requires Wine for Havok.dll):
//   dotnet publish -c Release -r win-x64 --self-contained -o win/
//   cd win && wine MwmCollisionExtractor.exe
// Build (Windows):
//   dotnet publish -c Release -o publish/
//   cd publish && MwmCollisionExtractor.exe

using System.Xml.Linq;
using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Havok;
using VRage.Library.Threading;
using VRageMath;
using LLE;
using ProtoBuf;

namespace MwmCollisionExtractor
{
    // Dummy ISharedCriticalSection — no real locking needed for single-threaded offline parsing
    sealed class NullSharedCriticalSection : ISharedCriticalSection
    {
        public SharedCriticalSection_UniqueLock EnterUnique() => new(this);
        public SharedCriticalSection_SharedLock EnterShared() => new(this);
        public void LeaveUnique_Internal() { }
        public void LeaveShared_Internal() { }
        public void Dispose() { }
    }

    static class Program
    {
        // Resolve native Havok.dll from Bin64 alongside managed assemblies
        static Program()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var name = new AssemblyName(args.Name).Name;
                string path = Path.Combine(Bin64, name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };

            // Register protobuf-net serializers for VRageMath types
            // Field numbers match the game's protobuf schema (stride 3 per column)
            var model = ProtoBuf.Meta.RuntimeTypeModel.Default;
            var vec3 = model.Add(typeof(Vector3), false);
            vec3.Add(1, "X"); vec3.Add(4, "Y"); vec3.Add(7, "Z");
            
            var mat = model.Add(typeof(Matrix), false);
            mat.Add(1, "M11"); mat.Add(4, "M12"); mat.Add(7, "M13"); mat.Add(10, "M14");
            mat.Add(13, "M21"); mat.Add(16, "M22"); mat.Add(19, "M23"); mat.Add(22, "M24");
            mat.Add(25, "M31"); mat.Add(28, "M32"); mat.Add(31, "M33"); mat.Add(34, "M34");
            mat.Add(37, "M41"); mat.Add(40, "M42"); mat.Add(43, "M43"); mat.Add(46, "M44");
        }


        const string Base = "/home/cat/Projects/";
        const string Bin64 = Base + "SpaceEngineers/Bin64";
        const float PhysicsConvexRadius = 0; //0.05f; // MyPerGameSettings.PhysicsConvexRadius

        private class BlockInfo
        {
            public DefinitionIdAsText DefId;
            public string ModelPath;
            public XElement Def;
            public string CubeSize;
            public Vector3I Size;
        }

        static void Main(string[] args)
        {
            string outputFile = Base + "LLE/LLE/Data/collisions.bin";
            const string gameRoot = Base + "SpaceEngineers/Content";
            const string sbcDir = Base + "SpaceEngineers/Content/Data/CubeBlocks";

            HkBaseSystem.Init(5 * 1024 * 1024, msg => { }, deepProfiling: false, new NullSharedCriticalSection());
            try
            {
                var blocks = LoadBlocks(sbcDir);
                var allGeometry = ExtractCollisions(blocks, gameRoot);
                SaveCollisions(allGeometry, outputFile);
            }
            finally
            {
                HkBaseSystem.Quit();
            }
        }

        static List<BlockInfo> LoadBlocks(string sbcDir)
        {
            var allBlocks = new List<BlockInfo>();
            foreach (var file in Directory.GetFiles(sbcDir, "*.sbc"))
            {
                Console.WriteLine($"Processing {Path.GetFileName(file)}...");
                XDocument doc = XDocument.Load(file);
                var blocks = doc.Descendants("Definition").Select(def =>
                {
                    string typeId = def.Element("Id")?.Element("TypeId")?.Value;
                    if (string.IsNullOrEmpty(typeId)) return null;
                    string subtype = def.Element("Id")?.Element("SubtypeId")?.Value ?? "";
                    var defId = new DefinitionIdAsText { TypeId = typeId, SubtypeId = subtype };

                    string modelPath = def.Element("Model")?.Value;
                    if (string.IsNullOrEmpty(modelPath))
                    {
                        var sides = def.Element("CubeDefinition")?.Element("Sides")?.Elements("Side").ToList();
                        if (sides != null && sides.Count > 0)
                        {
                            var firstModel = sides[0].Attribute("Model")?.Value;
                            if (sides.All(s => s.Attribute("Model")?.Value == firstModel))
                                modelPath = firstModel;
                        }
                    }

                    // Blocks without model but with BlockTopology==Cube still need skeleton collision
                    string blockTopology = def.Element("BlockTopology")?.Value;
                    if (string.IsNullOrEmpty(modelPath) && blockTopology != "Cube")
                        return null;
                    string cubeSize = def.Element("CubeSize")?.Value ?? "Large";
                    var sizeEl = def.Element("Size");
                    var size = new Vector3I(
                        int.Parse(sizeEl?.Attribute("x")?.Value ?? "1"),
                        int.Parse(sizeEl?.Attribute("y")?.Value ?? "1"),
                        int.Parse(sizeEl?.Attribute("z")?.Value ?? "1"));
                    return new BlockInfo { DefId = defId, ModelPath = modelPath, Def = def, CubeSize = cubeSize, Size = size };
                }).Where(b => b != null).ToList();
                allBlocks.AddRange(blocks);
            }
            return allBlocks;
        }

        static Dictionary<DefinitionIdAsText, CollisionGeometry> ExtractCollisions(List<BlockInfo> blocks, string gameRoot)
        {
            var allGeometry = new Dictionary<DefinitionIdAsText, CollisionGeometry>();
            foreach (var block in blocks)
            {
                // Phase 1: Try loading collision from model file
                CollisionGeometry geometry = null;
                if (!string.IsNullOrEmpty(block.ModelPath))
                {
                    string fullPath = Path.Combine(gameRoot, block.ModelPath.Replace('\\', '/'));
                    fullPath = FindFileCaseInsensitive(fullPath);
                    if (fullPath == null)
                    {
                        Console.WriteLine($"  SKIP {block.DefId.TypeId}:{block.DefId.SubtypeId}: model not found: {fullPath}");
                        continue;
                    }

                    var shapes = new List<HkShape>();
                    if (!LoadMwmShapes(fullPath, shapes, out string loadError))
                    {
                        Console.WriteLine($"  FAIL {block.DefId.TypeId}:{block.DefId.SubtypeId}: {loadError}");
                        continue;
                    }

                    geometry = new CollisionGeometry();
                    foreach (var s in shapes)
                        Flatten(s, Matrix.Identity, geometry.Shapes);
                }

                // Phase 2: Fallback to skeleton if model produced no shapes
                if (geometry == null || geometry.Shapes.Count == 0)
                {
                    if (geometry != null)
                        Console.WriteLine($"  EMPTY {block.DefId.TypeId}:{block.DefId.SubtypeId}: model collision is empty");

                    if (BuildSkeletonConvex(block.Def, block.CubeSize, out var skeletonGeom))
                    {
                        geometry = skeletonGeom;
                        Console.WriteLine($"  SKELETON {block.DefId.TypeId}:{block.DefId.SubtypeId}: {skeletonGeom.Shapes.Count} shapes");
                    }
                    else
                    {
                        // Phase 3: Fallback to AABB box by block Size
                        float gridSize = block.CubeSize == "Small" ? 0.5f : 2.5f;
                        geometry = new CollisionGeometry();
                        geometry.Shapes.Add(new BoxShape
                        {
                            HalfExtents = new Vector3(
                                block.Size.X * gridSize * 0.5f,
                                block.Size.Y * gridSize * 0.5f,
                                block.Size.Z * gridSize * 0.5f)
                        });
                        Console.WriteLine($"  BOX {block.DefId.TypeId}:{block.DefId.SubtypeId}: {block.Size} * {gridSize}");
                    }
                }
                else
                {
                    Console.WriteLine($"  OK {block.DefId.TypeId}:{block.DefId.SubtypeId}: {geometry.Shapes.Count} shapes");
                }

                allGeometry[block.DefId] = geometry;
            }
            return allGeometry;
        }

        static void SaveCollisions(Dictionary<DefinitionIdAsText, CollisionGeometry> allGeometry, string outputFile)
        {
            using (var fs = File.Create(outputFile))
                Serializer.Serialize(fs, allGeometry);
            Console.WriteLine($"\nSaved {allGeometry.Count} blocks to {outputFile}");
        }

        static void Flatten(HkShape shape, Matrix currentTransform, List<CollisionShape> result)
        {
            if (!shape.IsValid) return;

            switch (shape.ShapeType)
            {
                case HkShapeType.ConvexTranslate:
                    var translate = (HkConvexTranslateShape)shape;
                    Flatten(translate.ChildShape.Base, currentTransform * Matrix.CreateTranslation(translate.Translation), result);
                    break;

                case HkShapeType.ConvexTransform:
                    var transform = (HkConvexTransformShape)shape;
                    Flatten(transform.ChildShape.Base, currentTransform * transform.Transform, result);
                    break;

                case HkShapeType.StaticCompound:
                    var sCompound = (HkStaticCompoundShape)shape;
                    for (int i = 0; i < sCompound.InstanceCount; i++)
                        Flatten(sCompound.GetInstance(i), currentTransform * sCompound.GetInstanceTransform(i), result);
                    break;

                default:
                    if (shape.IsContainer())
                    {
                        var iter = shape.GetContainer();
                        while (iter.IsValid)
                        {
                            Flatten(iter.CurrentValue, currentTransform, result);
                            iter.Next();
                        }
                    }
                    else
                    {
                        var leaf = CreateLeaf(shape);
                        if (leaf != null)
                        {
                            leaf.Transform = currentTransform;
                            result.Add(leaf);
                        }
                    }
                    break;
            }
        }

        static CollisionShape CreateLeaf(HkShape shape)
        {
            switch (shape.ShapeType)
            {
                case HkShapeType.Box:
                    var box = (HkBoxShape)shape;
                    return new BoxShape { HalfExtents = box.HalfExtents + PhysicsConvexRadius };
                case HkShapeType.Sphere:
                    var sphere = (HkSphereShape)shape;
                    return new SphereShape { Radius = sphere.Radius + PhysicsConvexRadius };
                case HkShapeType.Capsule:
                    var capsule = (HkCapsuleShape)shape;
                    return new CapsuleShape { VertexA = capsule.VertexA, VertexB = capsule.VertexB, Radius = capsule.Radius + PhysicsConvexRadius };
                case HkShapeType.Cylinder:
                    var cylinder = (HkCylinderShape)shape;
                    // xx PhysicsConvexRadius ?
                    return new CylinderShape { VertexA = cylinder.VertexA, VertexB = cylinder.VertexB, Radius = cylinder.Radius };
                case HkShapeType.ConvexVertices:
                    var convex = (HkConvexVerticesShape)shape;
                    Vector3[] verts;
                    convex.GetVertices(out verts);
                    var hull = new ConvexHullShape();
                    // Approximate: push vertices outward from centroid (not exact Minkowski sum)

                    var centroid = Vector3.Zero;
                    foreach (var v in verts) centroid += v;
                    centroid /= verts.Length;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        var v = verts[i] - centroid;
                        if(v.Length() == 0) throw new Exception();
                        verts[i] = centroid + Vector3.Normalize(v) * (v.Length() + PhysicsConvexRadius);
                    }

                    hull.Vertices.AddRange(verts);
                    return hull;
                default:
                    return null;
            }
        }


        // Case-insensitive file lookup for Linux (Windows paths in SBC use mixed case)
        static string FindFileCaseInsensitive(string path)
        {
            if (File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);
            if (!Directory.Exists(dir)) return null;
            var match = Directory.GetFiles(dir).FirstOrDefault(f =>
                string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
            return match;
        }

        // MWM binary format parser — extracts HavokCollisionGeometry tag and loads shapes from it
        static bool LoadMwmShapes(string filePath, List<HkShape> outShapes, out string error)
        {
            using var fs = File.OpenRead(filePath);
            using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8);

            reader.ReadString(); // Skip first tag name
            int strCount = reader.ReadInt32();
            for (int i = 0; i < strCount; i++)
                reader.ReadString(); // Skip header strings

            int itemsCount = reader.ReadInt32();
            var indexDict = new Dictionary<string, int>();
            for (int i = 0; i < itemsCount; i++)
                indexDict[reader.ReadString()] = reader.ReadInt32();

            reader.BaseStream.Seek(indexDict["HavokCollisionGeometry"], SeekOrigin.Begin);
            reader.ReadString(); // Skip tag name
            int dataLen = reader.ReadInt32();
            if (dataLen == 0)
            {
                error = null;
                return true;
            }

            byte[] collisionData = reader.ReadBytes(dataLen);
            if (!HkShapeLoader.LoadShapesListFromBuffer(collisionData, outShapes, out bool containsScene, out bool containsDestruction))
            {
                error = "HkShapeLoader failed";
                return false;
            }

            error = null;
            return true;
        }

        static Vector3 DenormalizeBoneOffset(byte x, byte y, byte z, float gridSize)
        {
            float eps = 0.5f / 255.0f;
            return new Vector3(
                (x / 255.0f - (0.5f - eps)) * gridSize,
                (y / 255.0f - (0.5f - eps)) * gridSize,
                (z / 255.0f - (0.5f - eps)) * gridSize);
        }

        static bool BuildSkeletonConvex(XElement def, string cubeSize, out CollisionGeometry geometry)
        {
            geometry = null;

            var skeleton = def.Element("Skeleton");
            if (skeleton == null)
                return false;

            var cubeTopologyStr = def.Element("CubeDefinition")?.Element("CubeTopology")?.Value ?? "Box";
            if (!Enum.TryParse(cubeTopologyStr, true, out CubeTopology topology))
                return false;

            float gridSize = cubeSize == "Small" ? 0.5f : 2.5f;

            // Parse bone offsets: BonePosition → denormalized offset
            var boneOffsets = new Dictionary<Vector3I, Vector3>();
            foreach (var boneInfo in skeleton.Elements("BoneInfo"))
            {
                var posEl = boneInfo.Element("BonePosition");
                var offEl = boneInfo.Element("BoneOffset");
                if (posEl == null || offEl == null)
                    continue;

                var px = posEl.Attribute("X")?.Value;
                var py = posEl.Attribute("Y")?.Value;
                var pz = posEl.Attribute("Z")?.Value;
                var ox = offEl.Attribute("X")?.Value;
                var oy = offEl.Attribute("Y")?.Value;
                var oz = offEl.Attribute("Z")?.Value;
                if (px == null || py == null || pz == null || ox == null || oy == null || oz == null)
                    continue;

                var pos = new Vector3I(int.Parse(px), int.Parse(py), int.Parse(pz));
                var offset = DenormalizeBoneOffset(byte.Parse(ox), byte.Parse(oy), byte.Parse(oz), gridSize);
                boneOffsets[pos] = offset;
            }

            // Build world vertices from topology + bone offsets
            var blockVerts = CubeTopologyVertices.GetVertices(topology);
            var gridSizeHalf = gridSize * 0.5f;
            var worldVerts = new List<Vector3>(blockVerts.Length);

            foreach (var point in blockVerts)
            {
                var pointBonePos = new Vector3I(1, 1, 1) + Vector3I.Round(point);
                var vert = point * gridSizeHalf;
                if (boneOffsets.TryGetValue(pointBonePos, out var boneOffset))
                    vert += boneOffset;
                worldVerts.Add(vert);
            }

            geometry = new CollisionGeometry();
            geometry.Shapes.Add(new ConvexHullShape { Vertices = worldVerts });
            return true;
        }
    }
}
