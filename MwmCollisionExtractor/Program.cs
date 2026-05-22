// MwmCollisionExtractor — extracts collision shapes from .hkt / .mwm files
// Usage: MwmCollisionExtractor.exe <path/to/file.hkt or .mwm> [output.scad]
//
// Build (Linux, requires Wine for Havok.dll):
//   dotnet publish -c Release -r win-x64 --self-contained -o win/
//   cd win && wine MwmCollisionExtractor.exe <file.mwm> [output.scad]
//
// Build (Windows):
//   dotnet publish -c Release -o publish/
//   cd publish && MwmCollisionExtractor.exe <file.mwm> [output.scad]

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
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: MwmCollisionExtractor.exe <path/to/file.hkt or .mwm> [output.bin]");
                return;
            }

            string filePath = Path.GetFullPath(args[0]);
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"File not found: {filePath}");
                return;
            }

            string outputPath = null;
            if (args.Length >= 2)
            {
                outputPath = args[1];
            }

            // Initialize Havok base system (required before any shape operations)
            HkBaseSystem.Init(5 * 1024 * 1024, msg => { }, deepProfiling: false, new NullSharedCriticalSection());

            try
            {
                var geometry = new CollisionGeometry();
                var shapes = new List<HkShape>();
                bool ok;

                if (filePath.EndsWith(".mwm", StringComparison.OrdinalIgnoreCase))
                    ok = LoadMwmShapes(filePath, shapes);
                else
                    ok = HkShapeLoader.LoadShapesListFromFile(filePath, shapes);

                if (!ok || shapes.Count == 0)
                {
                    Console.Error.WriteLine($"Failed to load shapes from: {filePath}");
                    return;
                }

                Console.WriteLine($"Loaded {shapes.Count} root shape(s) from {Path.GetFileName(filePath)}");
                Console.WriteLine(new string('-', 60));

                for (int i = 0; i < shapes.Count; i++)
                {
                    Flatten(shapes[i], Matrix.Identity, geometry.Shapes);
                }
                Console.WriteLine(new string('-', 60));
                Console.WriteLine($"Total shapes extracted: {geometry.Shapes.Count}");
                foreach (var s in geometry.Shapes)
                    Console.WriteLine(s.ToString());
                Console.WriteLine(new string('-', 60));
                Console.WriteLine(new string('-', 60));

                if (outputPath != null)
                {
                    using (var fs = File.Create(outputPath))
                    {
                        Serializer.Serialize(fs, geometry);
                    }
                    Console.WriteLine($"Saved geometry to: {outputPath}");
                }
            }
            finally
            {
                HkBaseSystem.Quit();
            }
        }

        static void Flatten(HkShape shape, Matrix currentTransform, List<CollisionShape> result)
        {
            if (!shape.IsValid) return;

            // --- Wrapper shapes (transform + child) ---
            if (shape.ShapeType == HkShapeType.ConvexTranslate || shape.ShapeType == HkShapeType.ConvexTransform)
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
        static bool LoadMwmShapes(string filePath, List<HkShape> outShapes)
        {
            using (var fs = File.OpenRead(filePath))
            using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                // Read first tag name and its string array values
                string firstTag = reader.ReadString();
                int strCount = reader.ReadInt32();
                for (int i = 0; i < strCount; i++)
                {
                    string val = reader.ReadString();

                    // Parse version from "Version:X"
                    if (val.StartsWith("Version:"))
                    {
                        int version = int.Parse(val[8..]);
                        if (version >= 01066002)
                            return ReadMwmNewVersion(reader, outShapes);

                        Console.Error.WriteLine($"Old MWM version {version} is not supported.");
                        return false;
                    }
                }
            }

            Console.Error.WriteLine("Unsupported MWM format");
            return false;
        }

        static bool ReadMwmNewVersion(BinaryReader reader, List<HkShape> outShapes)
        {
            // Index dictionary: tag name -> file offset
            int itemsCount = reader.ReadInt32();
            var indexDict = new Dictionary<string, int>();
            for (int i = 0; i < itemsCount; i++)
            {
                string tagName = reader.ReadString();
                int offset = reader.ReadInt32();
                indexDict[tagName] = offset;
            }

            // Look for HavokCollisionGeometry tag
            if (!indexDict.TryGetValue("HavokCollisionGeometry", out int collisionOffset))
            {
                Console.Error.WriteLine("No HavokCollisionGeometry tag in MWM");
                return false;
            }

            // Seek to the tag data and read byte array
            reader.BaseStream.Seek(collisionOffset, SeekOrigin.Begin);
            string tagCheck = reader.ReadString();
            if (tagCheck != "HavokCollisionGeometry")
            {
                Console.Error.WriteLine($"Tag mismatch at offset {collisionOffset}: expected HavokCollisionGeometry, got '{tagCheck}'");
                return false;
            }

            int dataLen = reader.ReadInt32();
            byte[] collisionData = reader.ReadBytes(dataLen);

            // Load shapes from the embedded Havok buffer
            bool containsScene; 
            bool containsDestruction;
            if (!HkShapeLoader.LoadShapesListFromBuffer(collisionData, outShapes, out containsScene, out containsDestruction))
                return false;

            return true;
        }
    }
}
