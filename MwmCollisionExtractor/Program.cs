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
        }

        const string Bin64 = "/home/cat/Projects/SpaceEngineers/Bin64";
        const float PHYSICS_CONVEX_RADIUS = 0.05f;

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: MwmCollisionExtractor.exe <path/to/file.hkt or .mwm> [output.scad]");
                return;
            }

            string filePath = Path.GetFullPath(args[0]);
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"File not found: {filePath}");
                return;
            }

            TextWriter scadWriter = null;
            if (args.Length >= 2)
            {
                scadWriter = new StreamWriter(args[1]);
            }

            // Initialize Havok base system (required before any shape operations)
            HkBaseSystem.Init(5 * 1024 * 1024, msg => { }, deepProfiling: false, new NullSharedCriticalSection());

            try
            {
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
                    DumpShape(shapes[i], MatrixD.Identity, $"root[{i}]", 0, scadWriter);

                Console.WriteLine(new string('-', 60));
            }
            finally
            {
                HkBaseSystem.Quit();
                scadWriter?.Dispose();
            }
        }

        static void DumpShape(HkShape shape, MatrixD parentMatrix, string label, int depth, TextWriter scadWriter = null)
        {
            if (!shape.IsValid) return;

            string indent = new string(' ', depth * 2);
            Vector3 pos = parentMatrix.Translation;

            // OpenSCAD multmatrix wrapper
            if (scadWriter != null)
            {
                scadWriter.Write($"multmatrix([ [{parentMatrix.M11:F4}, {parentMatrix.M21:F4}, {parentMatrix.M31:F4}, {parentMatrix.M41:F4}], [{parentMatrix.M12:F4}, {parentMatrix.M22:F4}, {parentMatrix.M32:F4}, {parentMatrix.M42:F4}], [{parentMatrix.M13:F4}, {parentMatrix.M23:F4}, {parentMatrix.M33:F4}, {parentMatrix.M43:F4}], [0, 0, 0, 1] ]) {{\n");
            }
            // --- Wrapper shapes (transform + child) — check before IsContainer() ---
            if (shape.ShapeType == HkShapeType.ConvexTranslate || shape.ShapeType == HkShapeType.ConvexTransform)
            {
                try
                {
                    var cts = (HkConvexTranslateShape)shape;
                    var childShape = cts.ChildShape.Base;
                    MatrixD childMatrix = parentMatrix * MatrixD.CreateTranslation(cts.Translation);
                    Console.WriteLine($"{indent}{label} [{shape.ShapeType}] trans=({cts.Translation.X:F2}, {cts.Translation.Y:F2}, {cts.Translation.Z:F2})");
                    DumpShape(childShape, childMatrix, "child", depth + 1, scadWriter);
                }
                catch { /* not ConvexTranslateShape — skip */ }
            }
            // --- Container shapes (hierarchy nodes) ---
            else if (shape.IsContainer())
            {
                var type = shape.ShapeType;

                if (type == HkShapeType.StaticCompound)
                {
                    var compound = (HkStaticCompoundShape)shape;
                    Console.WriteLine($"{indent}{label} [{type}] pos=({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}) instances={compound.InstanceCount}");

                    for (int i = 0; i < compound.InstanceCount; i++)
                    {
                        Matrix instTransform = compound.GetInstanceTransform(i);
                        // StaticCompound uses single-precision Matrix, convert to double
                        MatrixD childMatrix = new MatrixD(
                            instTransform.M11, instTransform.M12, instTransform.M13, instTransform.M14,
                            instTransform.M21, instTransform.M22, instTransform.M23, instTransform.M24,
                            instTransform.M31, instTransform.M32, instTransform.M33, instTransform.M34,
                            instTransform.M41, instTransform.M42, instTransform.M43, instTransform.M44);
                        childMatrix = parentMatrix * childMatrix;

                        HkShape child = compound.GetInstance(i);
                        DumpShape(child, childMatrix, $"inst[{i}]", depth + 1, scadWriter);
                    }
                }
                else if (shape.ShapeType == HkShapeType.ConvexList || shape.ShapeType == HkShapeType.Collection)
                {
                    Console.WriteLine($"{indent}{label} [{type}] pos=({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");
                    var iter = shape.GetContainer();
                    while (iter.IsValid)
                    {
                        uint key = iter.CurrentShapeKey;
                        HkShape child = iter.CurrentValue;
                        DumpShape(child, parentMatrix, $"child[key={key}]", depth + 1, scadWriter);
                        iter.Next();
                    }
                }
                else
                {
                    // Generic container — iterate via GetContainer()
                    Console.WriteLine($"{indent}{label} [{type}] pos=({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");

                    var iter = shape.GetContainer();
                    while (iter.IsValid)
                    {
                        uint key = iter.CurrentShapeKey;
                        HkShape child = iter.CurrentValue;
                        DumpShape(child, parentMatrix, $"child[key={key}]", depth + 1, scadWriter);
                        iter.Next();
                    }
                }
            }
            // --- Leaf shapes ---
            else
            {
                Console.Write($"{indent}{label} [{shape.ShapeType}] pos=({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");

                switch (shape.ShapeType)
                {
                    case HkShapeType.Box:
                        var box = (HkBoxShape)shape;
                        Console.WriteLine($" halfExtents=({box.HalfExtents.X:F3}, {box.HalfExtents.Y:F3}, {box.HalfExtents.Z:F3})");
                        if (scadWriter != null)
                            scadWriter.WriteLine($"cube(size=[{(box.HalfExtents.X + PHYSICS_CONVEX_RADIUS)*2:F4}, {(box.HalfExtents.Y + PHYSICS_CONVEX_RADIUS)*2:F4}, {(box.HalfExtents.Z + PHYSICS_CONVEX_RADIUS)*2:F4}], center=true);");
                        break;

                    case HkShapeType.Sphere:
                        var sphere = (HkSphereShape)shape;
                        Console.WriteLine($" radius={sphere.Radius:F3}");
                        if (scadWriter != null)
                            scadWriter.WriteLine($"sphere(r={sphere.Radius:F4});");
                        break;


                    case HkShapeType.Capsule:
                        var capsule = (HkCapsuleShape)shape;
                        Console.WriteLine($" A=({capsule.VertexA.X:F2}, {capsule.VertexA.Y:F2}, {capsule.VertexA.Z:F2}) B=({capsule.VertexB.X:F2}, {capsule.VertexB.Y:F2}, {capsule.VertexB.Z:F2}) radius={capsule.Radius:F3}");
                        break;

                    case HkShapeType.Cylinder:
                        var cylinder = (HkCylinderShape)shape;
                        Console.WriteLine($" A=({cylinder.VertexA.X:F2}, {cylinder.VertexA.Y:F2}, {cylinder.VertexA.Z:F2}) B=({cylinder.VertexB.X:F2}, {cylinder.VertexB.Y:F2}, {cylinder.VertexB.Z:F2}) radius={cylinder.Radius:F3}");
                        break;

                    case HkShapeType.ConvexVertices:
                        try
                        {
                            var convex = (HkConvexVerticesShape)shape;
                            int vCount = convex.VertexCount;
                            Console.Write($" vertices={vCount} [");

                            Vector3[] verts;
                            convex.GetVertices(out verts);

                            for (int v = 0; v < verts.Length; v++)
                                Console.Write((v > 0 ? ", " : "") + $"({verts[v].X:F2},{verts[v].Y:F2},{verts[v].Z:F2})");
                            Console.WriteLine("]");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($" [Error: {ex.Message}]");
                        }
                        break;

                    default:
                        shape.GetLocalAABB(0f, out Vector4 aabbMinD, out Vector4 aabbMaxD);
                        Console.WriteLine($" AABB=[({aabbMinD.X:F2},{aabbMinD.Y:F2},{aabbMinD.Z:F2})..({aabbMaxD.X:F2},{aabbMaxD.Y:F2},{aabbMaxD.Z:F2})]");
                        break;
                }
            }

            if (scadWriter != null)
                scadWriter.WriteLine("}");
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
                        int version = int.Parse(val.Substring(8));
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
