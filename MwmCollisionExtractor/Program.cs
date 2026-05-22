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
            var model = ProtoBuf.Meta.RuntimeTypeModel.Default;
            var vec3 = model.Add(typeof(Vector3), false);
            vec3.Add(1, "X"); vec3.Add(4, "Y"); vec3.Add(7, "Z");
            // Field numbers match the game's protobuf schema (stride 3 per column)
            var mat = model.Add(typeof(Matrix), false);
            var props = typeof(Matrix).GetProperties();
            for (int i = 0; i < props.Length; i++)
                mat.Add(1 + i * 3, props[i].Name);
        }

        const string Bin64 = "/home/cat/Projects/SpaceEngineers/Bin64";
        const float PHYSICS_CONVEX_RADIUS = 0.05f;

        static void Main(string[] args)
        {
            string outputPath = args.Length > 0 ? args[0] : "/home/cat/Projects/LLE/LLE/Data/collisions.bin";
            RunBatch(outputPath);
        }

        static void Flatten(HkShape shape, Matrix currentTransform, List<CollisionShape> result)
        {
            if (!shape.IsValid) return;

            if (shape.ShapeType == HkShapeType.ConvexTranslate)
            {
                var cts = (HkConvexTranslateShape)shape;
                Flatten(cts.ChildShape.Base, currentTransform * Matrix.CreateTranslation(cts.Translation), result);
            }
            else if (shape.ShapeType == HkShapeType.ConvexTransform)
            {
                var cts = (HkConvexTransformShape)shape;
                Flatten(cts.ChildShape.Base, currentTransform * cts.Transform, result);
            }
            else if (shape.IsContainer())
            {
                if (shape.ShapeType == HkShapeType.StaticCompound)
                {
                    var sCompound = (HkStaticCompoundShape)shape;
                    for (int i = 0; i < sCompound.InstanceCount; i++)
                        Flatten(sCompound.GetInstance(i), currentTransform * sCompound.GetInstanceTransform(i), result);
                }
                else
                {
                    var iter = shape.GetContainer();
                    while (iter.IsValid)
                    {
                        Flatten(iter.CurrentValue, currentTransform, result);
                        iter.Next();
                    }
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
        }

        static CollisionShape CreateLeaf(HkShape shape)
        {
            switch (shape.ShapeType)
            {
                case HkShapeType.Box:
                    var box = (HkBoxShape)shape;
                    return new BoxShape { HalfExtents = box.HalfExtents };
                case HkShapeType.Sphere:
                    var sphere = (HkSphereShape)shape;
                    return new SphereShape { Radius = sphere.Radius };
                case HkShapeType.Capsule:
                    var capsule = (HkCapsuleShape)shape;
                    return new CapsuleShape { VertexA = capsule.VertexA, VertexB = capsule.VertexB, Radius = capsule.Radius };
                case HkShapeType.Cylinder:
                    var cylinder = (HkCylinderShape)shape;
                    return new CylinderShape { VertexA = cylinder.VertexA, VertexB = cylinder.VertexB, Radius = cylinder.Radius };
                case HkShapeType.ConvexVertices:
                    var convex = (HkConvexVerticesShape)shape;
                    Vector3[] verts;
                    convex.GetVertices(out verts);
                    var hull = new ConvexHullShape();
                    hull.Vertices.AddRange(verts);
                    return hull;
                default:
                    return null;
            }
        }

        // MWM binary format parser — extracts HavokCollisionGeometry tag and loads shapes from it
        static bool LoadMwmShapes(string filePath, List<HkShape> outShapes, out string error)
        {
            using (var fs = File.OpenRead(filePath))
            using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true))
            {
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
                bool containsScene; 
                bool containsDestruction;
                if (!HkShapeLoader.LoadShapesListFromBuffer(collisionData, outShapes, out containsScene, out containsDestruction))
                {
                    error = "HkShapeLoader failed";
                    return false;
                }

                error = null;
                return true;
            }
        }
        static void RunBatch(string outputFile)
        {
            const string sbcDir = "/home/cat/Projects/SpaceEngineers/Content/Data/CubeBlocks";
            const string gameRoot = "/home/cat/Projects/SpaceEngineers/Content";
            
            HkBaseSystem.Init(5 * 1024 * 1024, msg => { }, deepProfiling: false, new NullSharedCriticalSection());
            try
            {
                var allGeometry = new Dictionary<DefinitionIdAsText, CollisionGeometry>();
                var sbcFiles = Directory.GetFiles(sbcDir, "*.sbc");
                
                foreach (var file in sbcFiles)
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
                        
                        return string.IsNullOrEmpty(modelPath) ? null : new { DefId = defId, ModelPath = modelPath };
                    }).Where(b => b != null).ToList();

                    foreach (var block in blocks)
                    {
                        string fullPath = Path.Combine(gameRoot, block.ModelPath.Replace('\\', '/'));
                        if (!File.Exists(fullPath))
                        {
                            Console.WriteLine($"  SKIP {block.DefId.TypeId}:{block.DefId.SubtypeId}: model not found: {fullPath}");
                            continue;
                        }
                        
                        var shapes = new List<HkShape>();
                        if (LoadMwmShapes(fullPath, shapes, out string loadError))
                        {
                            var geometry = new CollisionGeometry();
                            foreach (var s in shapes)
                                Flatten(s, Matrix.Identity, geometry.Shapes);
                            
                            allGeometry[block.DefId] = geometry;
                            if (geometry.Shapes.Count == 0)
                                Console.WriteLine($"  NO COLLISION {block.DefId.TypeId}:{block.DefId.SubtypeId}");
                            else
                                Console.WriteLine($"  OK {block.DefId.TypeId}:{block.DefId.SubtypeId}: {geometry.Shapes.Count} shapes");
                        }
                        else
                        {
                            Console.WriteLine($"  FAIL {block.DefId.TypeId}:{block.DefId.SubtypeId}: {loadError}");
                        }
                    }
                }
                
                using (var fs = File.Create(outputFile))
                {
                    Serializer.Serialize(fs, allGeometry);
                }
                Console.WriteLine($"\nBatch complete. Saved {allGeometry.Count} blocks to {outputFile}");
            }
            finally
            {
                HkBaseSystem.Quit();
            }
        }
    }
}
