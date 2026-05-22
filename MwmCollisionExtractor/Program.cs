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
            var mat = model.Add(typeof(Matrix), false);
            mat.Add(1, "M11"); mat.Add(4, "M12"); mat.Add(7, "M13"); mat.Add(10, "M14");
            mat.Add(13, "M21"); mat.Add(16, "M22"); mat.Add(19, "M23"); mat.Add(22, "M24");
            mat.Add(25, "M31"); mat.Add(28, "M32"); mat.Add(31, "M33"); mat.Add(34, "M34");
            mat.Add(37, "M41"); mat.Add(40, "M42"); mat.Add(43, "M43"); mat.Add(46, "M44");
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

            // --- Wrapper shapes (transform + child) ---
            if (shape.ShapeType == HkShapeType.ConvexTranslate)
            {
                try
                {
                    var cts = (HkConvexTranslateShape)shape;
                    var childShape = cts.ChildShape.Base;
                    Matrix nextTransform = currentTransform * Matrix.CreateTranslation(cts.Translation);
                    Flatten(childShape, nextTransform, result);
                }
                catch { }
            }
            else if (shape.ShapeType == HkShapeType.ConvexTransform)
            {
                try
                {
                    var cts = (HkConvexTransformShape)shape;
                    var childShape = cts.ChildShape.Base;
                    Matrix nextTransform = currentTransform * cts.Transform;
                    Flatten(childShape, nextTransform, result);
                }
                catch { }
            }
            // --- Container shapes (hierarchy nodes) ---
            else if (shape.IsContainer())
            {
                var type = shape.ShapeType;
                if (type == HkShapeType.StaticCompound)
                {
                    var sCompound = (HkStaticCompoundShape)shape;
                    for (int i = 0; i < sCompound.InstanceCount; i++)
                    {
                        Matrix instTransform = sCompound.GetInstanceTransform(i);
                        Flatten(sCompound.GetInstance(i), currentTransform * instTransform, result);
                    }
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
            // --- Leaf shapes ---
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
                    try
                    {
                        var convex = (HkConvexVerticesShape)shape;
                        Vector3[] verts;
                        convex.GetVertices(out verts);
                        var hull = new ConvexHullShape();
                        hull.Vertices.AddRange(verts);
                        return hull;
                    }
                    catch { return null; }
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
                string firstTag = reader.ReadString();
                int strCount = reader.ReadInt32();
                bool versionFound = false;
                for (int i = 0; i < strCount; i++)
                {
                    string val = reader.ReadString();
                    if (val.StartsWith("Version:"))
                    {
                        int version = int.Parse(val.Substring(8));
                        if (version < 01066002)
                        {
                            error = $"Old MWM version {version}";
                            return false;
                        }
                        versionFound = true;
                        break;
                    }
                }
                if (!versionFound)
                {
                    error = "Unsupported MWM format";
                    return false;
                }

                // Index dictionary: tag name -> file offset
                int itemsCount = reader.ReadInt32();
                var indexDict = new Dictionary<string, int>();
                for (int i = 0; i < itemsCount; i++)
                {
                    string tagName = reader.ReadString();
                    int offset = reader.ReadInt32();
                    indexDict[tagName] = offset;
                }

                if (!indexDict.TryGetValue("HavokCollisionGeometry", out int collisionOffset))
                {
                    error = "No HavokCollisionGeometry tag";
                    return false;
                }

                reader.BaseStream.Seek(collisionOffset, SeekOrigin.Begin);
                string tagCheck = reader.ReadString();
                if (tagCheck != "HavokCollisionGeometry")
                {
                    error = $"Tag mismatch: '{tagCheck}'";
                    return false;
                }

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
                    var definitions = doc.Descendants("Definition");
                    
                    foreach (var def in definitions)
                    {
                        string typeId = def.Element("Id")?.Element("TypeId")?.Value;
                        string subtype = def.Element("Id")?.Element("SubtypeId")?.Value;
                        if (string.IsNullOrEmpty(typeId)) continue;
                        if (string.IsNullOrEmpty(subtype)) subtype = "";
                        var defId = new DefinitionIdAsText { TypeId = typeId, SubtypeId = subtype };

                        string modelPath = def.Element("Model")?.Value;
                        
                        if (string.IsNullOrEmpty(modelPath))
                        {
                            var sides = def.Element("CubeDefinition")?.Element("Sides")?.Elements("Side").ToList();
                            if (sides != null && sides.Count > 0)
                            {
                                var firstModel = sides[0].Attribute("Model")?.Value;
                                if (sides.All(s => s.Attribute("Model")?.Value == firstModel))
                                {
                                    modelPath = firstModel;
                                }
                            }
                        }
                        
                        if (string.IsNullOrEmpty(modelPath)) continue;
                        
                        string fullPath = Path.Combine(gameRoot, modelPath.Replace('\\', '/'));
                        if (!File.Exists(fullPath))
                        {
                            Console.WriteLine($"  SKIP {defId.TypeId}:{defId.SubtypeId}: model not found: {fullPath}");
                            continue;
                        }
                        
                        var shapes = new List<HkShape>();
                        if (LoadMwmShapes(fullPath, shapes, out string loadError))
                        {
                            var geometry = new CollisionGeometry();
                            foreach (var s in shapes)
                            {
                                Flatten(s, Matrix.Identity, geometry.Shapes);
                            }
                            allGeometry[defId] = geometry;
                            if (geometry.Shapes.Count == 0)
                                Console.WriteLine($"  NO COLLISION {defId.TypeId}:{defId.SubtypeId}");
                            else
                                Console.WriteLine($"  OK {defId.TypeId}:{defId.SubtypeId}: {geometry.Shapes.Count} shapes");
                        }
                        else
                        {
                            Console.WriteLine($"  FAIL {defId.TypeId}:{defId.SubtypeId}: {loadError}");
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
