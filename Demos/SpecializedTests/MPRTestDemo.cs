﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.CollisionDetection.CollisionTasks;
using BepuPhysics.CollisionDetection.SweepTasks;
using BepuUtilities;
using BepuUtilities.Memory;
using DemoContentLoader;
using DemoRenderer;
using DemoRenderer.Constraints;
using DemoRenderer.UI;
using DemoUtilities;
using Quaternion = BepuUtilities.Quaternion;

namespace Demos.SpecializedTests
{
    public class MPRTestDemo : Demo
    {
        Buffer<LineInstance> shapeLines;
        MinkowskiSimplexes simplexes;
        Vector3 basePosition;
        Vector3 localNormal;
        //bool intersecting;
        Vector3 surfaceCastDirection;
        float t;

        public override void Initialize(ContentArchive content, Camera camera)
        {
            camera.Position = new Vector3(-13f, 6, -13f);
            camera.Yaw = MathF.PI * 3f / 4;
            camera.Pitch = MathF.PI * 0.05f;
            Simulation = Simulation.Create(BufferPool, new DemoNarrowPhaseCallbacks(), new DemoPoseIntegratorCallbacks(new Vector3(0, -10, 0)));

            var shapeA = new Cylinder(0.5f, 0.5f);
            var poseA = new RigidPose(new Vector3(0, 0, 0));
            var shapeB = new Cylinder(0.5f, 0.5f);
            var poseB = new RigidPose(new Vector3(0.5f, 0.5f, 0.5f), Quaternion.CreateFromAxisAngle(new Vector3(1, 0, 0), MathF.PI * 0.5f));

            basePosition = default;
            shapeLines = MinkowskiShapeVisualizer.CreateLines<Cylinder, CylinderWide, CylinderSupportFinder, Cylinder, CylinderWide, CylinderSupportFinder>(
                shapeA, shapeB, poseA, poseB, 65536,
                0.01f, new Vector3(0.4f, 0.4f, 0),
                0.1f, new Vector3(0, 1, 0), default, basePosition, BufferPool);

            var aWide = default(CylinderWide);
            var bWide = default(CylinderWide);
            aWide.Broadcast(shapeA);
            bWide.Broadcast(shapeB);
            var worldOffsetB = poseB.Position - poseA.Position;
            var localOrientationB = Matrix3x3.CreateFromQuaternion(Quaternion.Concatenate(poseB.Orientation, Quaternion.Conjugate(poseA.Orientation)));
            var localOffsetB = Quaternion.Transform(worldOffsetB, Quaternion.Conjugate(poseA.Orientation));
            Vector3Wide.Broadcast(localOffsetB, out var localOffsetBWide);
            Matrix3x3Wide.Broadcast(localOrientationB, out var localOrientationBWide);
            var cylinderSupportFinder = default(CylinderSupportFinder);
            simplexes = new MinkowskiSimplexes(BufferPool);
            //MPR<Cylinder, CylinderWide, CylinderSupportFinder, Cylinder, CylinderWide, CylinderSupportFinder>.Test(
            //    aWide, bWide, localOffsetBWide, localOrientationBWide, ref cylinderSupportFinder, ref cylinderSupportFinder, new Vector<float>(0.0001f), new Vector<int>(), out var intersecting, out var localNormal, simplexes);
            //this.intersecting = intersecting[0] < 0;
            //Vector3Wide.ReadSlot(ref localNormal, 0, out this.localNormal);
            //this.intersecting = intersecting[0] < 0;
            //Vector3Wide.ReadSlot(ref localNormal, 0, out this.localNormal);

            surfaceCastDirection = localOffsetB;
            Vector3Wide.Broadcast(surfaceCastDirection, out var surfaceCastDirectionWide);
            MPR<Cylinder, CylinderWide, CylinderSupportFinder, Cylinder, CylinderWide, CylinderSupportFinder>.LocalSurfaceCast(
                aWide, bWide, localOffsetBWide, localOrientationBWide, ref cylinderSupportFinder, ref cylinderSupportFinder, surfaceCastDirectionWide, new Vector<float>(1e-5f), new Vector<int>(), out var t, out var localNormal, simplexes, 100);

            //for (int i = 0; i < 2; ++i)
            //{
            //    Vector3Wide.Normalize(localNormal, out surfaceCastDirectionWide);
            //    Vector3Wide.ReadSlot(ref surfaceCastDirectionWide, 0, out surfaceCastDirection);
            //    simplexes.Clear();
            //    MPR<Cylinder, CylinderWide, CylinderSupportFinder, Cylinder, CylinderWide, CylinderSupportFinder>.LocalSurfaceCast(
            //        aWide, bWide, localOffsetBWide, localOrientationBWide, ref cylinderSupportFinder, ref cylinderSupportFinder, surfaceCastDirectionWide, new Vector<float>(1e-5f), new Vector<int>(), out t, out localNormal, simplexes, 100);
            //}

            var iterationCount = 100000;
            surfaceCastDirection = localOffsetB;
            Vector3Wide.Broadcast(surfaceCastDirection, out surfaceCastDirectionWide);
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterationCount; ++i)
            {
                MPR<Cylinder, CylinderWide, CylinderSupportFinder, Cylinder, CylinderWide, CylinderSupportFinder>.LocalSurfaceCast(
                    aWide, bWide, localOffsetBWide, localOrientationBWide, ref cylinderSupportFinder, ref cylinderSupportFinder, surfaceCastDirectionWide, new Vector<float>(1e-5f), new Vector<int>(), out t, out localNormal, null, 100);
            }
            var end = Stopwatch.GetTimestamp();
            Console.WriteLine($"Time (ns): {(end - start) * 1e9 / (iterationCount * Stopwatch.Frequency)}");
            this.t = t[0];
            Vector3Wide.ReadSlot(ref localNormal, 0, out this.localNormal);
        }

        int simplexIndex;
        public override void Update(Window window, Camera camera, Input input, float dt)
        {
            if (input.TypedCharacters.Contains('x'))
            {
                simplexIndex = Math.Max(0, simplexIndex - 1);
            }
            else if (input.TypedCharacters.Contains('c'))
            {
                simplexIndex = Math.Min(simplexIndex + 1, simplexes.SimplexCount - 1);
            }
            base.Update(window, camera, input, dt);
        }

        public override void Render(Renderer renderer, Camera camera, Input input, TextBuilder text, Font font)
        {
            MinkowskiShapeVisualizer.Draw(shapeLines, renderer);
            SimplexVisualizer.Draw(renderer, simplexes.GetSimplex(simplexIndex).points, basePosition, new Vector3(1, 0, 0), default);
            renderer.TextBatcher.Write(
                text.Clear().Append($"Enumerate simplexes with X and C. Current simplex: ").Append(simplexIndex + 1).Append(" out of ").Append(simplexes.SimplexCount),
                new Vector2(32, renderer.Surface.Resolution.Y - 80), 20, new Vector3(1), font);
            renderer.TextBatcher.Write(
                text.Clear().Append($"Simplex tag: ").Append(simplexes.GetSimplex(simplexIndex).tag),
                new Vector2(32, renderer.Surface.Resolution.Y - 60), 20, new Vector3(1), font);
            renderer.Lines.Allocate() = new LineInstance(basePosition, surfaceCastDirection, new Vector3(1), default);
            if (simplexIndex == simplexes.SimplexCount - 1)
            {
                //renderer.TextBatcher.Write(
                //    text.Clear().Append($"Terminated in state: ").Append(intersecting ? "intersecting" : "separated"),
                //    new Vector2(32, renderer.Surface.Resolution.Y - 40), 20, new Vector3(1), font);
                renderer.TextBatcher.Write(
                    text.Clear().Append($"T: ").Append(t, 8),
                    new Vector2(32, renderer.Surface.Resolution.Y - 40), 20, new Vector3(1), font);

                Vector3 simplexCenter = default;
                var simplex = simplexes.GetSimplex(simplexIndex).points;
                if (simplex.Length > 1)
                {
                    for (int i = 1; i < simplex.Length; ++i)
                    {
                        simplexCenter += simplex[i];
                    }
                    simplexCenter /= simplex.Length - 1;
                }
                simplexCenter += basePosition;
                renderer.Lines.Allocate() = new LineInstance(simplexCenter, simplexCenter + Vector3.Normalize(localNormal), new Vector3(0, 1, 1), default);
            }
            base.Render(renderer, camera, input, text, font);
        }
    }
}
