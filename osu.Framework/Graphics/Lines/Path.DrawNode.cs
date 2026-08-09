// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics.Primitives;
using osuTK;
using System;
using System.Collections.Generic;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Graphics.Shaders;
using System.Diagnostics;
using System.Runtime.InteropServices;
using osuTK.Graphics.ES30;

namespace osu.Framework.Graphics.Lines
{
    public partial class Path
    {
        private class PathDrawNode : DrawNode
        {
            private const float precision = 0.01f; // Smallest allowed segment length. Used for segment reduction algorithm.
            private const int max_res = 24;

            protected new Path Source => (Path)base.Source;

            private readonly List<Line> segments = new List<Line>();

            private float radius;
            private IShader? pathShader;

            private IVertexBatch<PathVertex>? quadBatch;

            public PathDrawNode(Path source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                segments.Clear();
                segments.AddRange(Source.segments);

                radius = Source.PathRadius;
                pathShader = Source.pathShader;
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (segments.Count == 0 || pathShader == null || radius == 0f)
                    return;

                // Size must be divisible by 4 such that the amount of vertices is a multiple of the amount of vertices
                // per primitive (quads in this case). Otherwise overflowing the batch will result in wrong
                // grouping of vertices into primitives.
                quadBatch ??= renderer.CreateQuadBatch<PathVertex>(9000, 10);

                renderer.PushLocalMatrix(DrawInfo.Matrix);

                renderer.SetBlend(new BlendingParameters
                {
                    Source = BlendingType.One,
                    Destination = BlendingType.One,
                    SourceAlpha = BlendingType.One,
                    DestinationAlpha = BlendingType.One,
                    RGBEquation = BlendingEquation.Max,
                    AlphaEquation = BlendingEquation.Max,
                });

                pathShader.Bind();

                updateVertexBuffer();

                pathShader.Unbind();

                renderer.PopLocalMatrix();
            }

            /// <summary>
            /// Modifies provided segments to create visually continuous path.
            /// </summary>
            /// <param name="segment">Current segment.</param>
            /// <param name="prevSegment">Previous segment.</param>
            /// <param name="location">Position of the <see cref="DrawableSegment"/>'s start point relative to the previous one.</param>
            /// <param name="endCap">Whether end cap must be added to the current segment.</param>
            private void connect(ref DrawableSegment segment, ref DrawableSegment prevSegment, SegmentStartLocation location, bool endCap)
            {
                // When segment starts outside the previous one, nothing is being connected to the start of the segment and start cap is required.
                bool startCap = location == SegmentStartLocation.Outside;

                Vector2 dir = segment.DirectionNormalized;
                Vector2 capOffset = dir * radius;

                // Segment starts at the end of the previous one - figure out the connection type
                if (location == SegmentStartLocation.End)
                {
                    Debug.Assert(prevSegment.EndPoint == segment.StartPoint);

                    Vector2 dir2 = -prevSegment.DirectionNormalized;
                    Vector2.Dot(ref dir, ref dir2, out float dot);

                    // Angle between segments is less than 90 degrees - don't draw anything and use segment start cap instead.
                    // Overdraw is inevitable anyway and this seems like a cheaper option than computing exact shape.
                    // Also by doing this we can further reduce vertex count.
                    if (dot >= 0)
                    {
                        startCap = true;
                    }
                    else // angle is more than 90 degrees - connect segments to each other
                    {
                        Vector2.PerpDot(ref dir, ref dir2, out float pDot);
                        float thetaDiff = Math.Abs(MathF.Atan(pDot / dot));
                        float intersectionDistance = radius * (float)Math.Tan(thetaDiff * 0.5);
                        Vector2 intersectionOffset = dir * intersectionDistance;
                        bool hasInnerIntersection = Math.Min(segment.LengthSquared, prevSegment.LengthSquared) > intersectionDistance * intersectionDistance;

                        if (pDot < 0f) // clockwise
                        {
                            // always connect top vertices
                            segment.TopLeft -= intersectionOffset;
                            prevSegment.TopRight = segment.TopLeft;

                            // connect bottom vertices only if there's an intersection between bottom edges
                            if (hasInnerIntersection)
                            {
                                segment.BottomLeft += intersectionOffset;
                                prevSegment.BottomRight = segment.BottomLeft;
                            }
                        }
                        else
                        {
                            segment.BottomLeft -= intersectionOffset;
                            prevSegment.BottomRight = segment.BottomLeft;

                            if (hasInnerIntersection)
                            {
                                segment.TopLeft += intersectionOffset;
                                prevSegment.TopRight = segment.TopLeft;
                            }
                        }
                    }
                }

                if (startCap)
                {
                    segment.TopLeft -= capOffset;
                    segment.BottomLeft -= capOffset;
                }

                if (endCap)
                {
                    segment.TopRight += capOffset;
                    segment.BottomRight += capOffset;
                }
            }

            private void drawSegment(ref DrawableSegment s)
            {
                Debug.Assert(quadBatch != null);

                quadBatch.Add(new PathVertex(s.TopLeft, s.StartPoint, s.EndPoint, radius));
                quadBatch.Add(new PathVertex(s.TopRight, s.StartPoint, s.EndPoint, radius));
                quadBatch.Add(new PathVertex(s.BottomRight, s.StartPoint, s.EndPoint, radius));
                quadBatch.Add(new PathVertex(s.BottomLeft, s.StartPoint, s.EndPoint, radius));
            }

            private void updateVertexBuffer()
            {
                Debug.Assert(segments.Count > 0);

                Line segmentToProcess = segments[0];

                SegmentStartLocation location = SegmentStartLocation.Outside;
                SegmentStartLocation nextLocation = SegmentStartLocation.End;

                // We initialize "fake" initial segment before the 0'th one (which we don't want to draw)
                // so that on first connect() call with current SegmentStartLocation parameters path start cap will be added.
                DrawableSegment segmentToDraw = new DrawableSegment(segments[0], radius);
                bool initialSkipped = false;

                // This loop tries to merge consecutive path segments which are located within the same line.
                // If next segment deviates from the one being processed we will save it for drawing later, after the new one will be fully processed
                // since the connection type between segments can be established only after we know "final" shape of them both.
                for (int i = 1; i < segments.Count; i++)
                {
                    Vector2 dir = segmentToProcess.Direction;
                    float lengthSquared = dir.X * dir.X + dir.Y * dir.Y;
                    Vector2 nextVertex = segments[i].EndPoint;

                    // If segment is too short, make its end point equal start point of a new segment
                    if (lengthSquared < precision)
                    {
                        segmentToProcess = new Line(segmentToProcess.StartPoint, nextVertex);
                        continue;
                    }

                    Vector2 dir2 = nextVertex - segmentToProcess.StartPoint;
                    Vector2.PerpDot(ref dir, ref dir2, out float pDot);

                    // Expand segment if next end point is located within a line passing through it (distance from the next vertex to the segment is less than precision)
                    if (pDot * pDot / lengthSquared < precision * precision)
                    {
                        nextLocation = SegmentStartLocation.StartOrMiddle;

                        Vector2.Dot(ref dir, ref dir2, out float dot);

                        // new vertex is located behind the segment start point, expand segment backwards
                        if (dot < 0)
                        {
                            segmentToProcess = new Line(nextVertex, segmentToProcess.EndPoint);
                            location = SegmentStartLocation.Outside;
                        }
                        else if (dot > lengthSquared) // new vertex is located in front of the end point, expand segment forward
                        {
                            segmentToProcess = new Line(segmentToProcess.StartPoint, nextVertex);
                            nextLocation = SegmentStartLocation.End;
                        }
                    }
                    else // Otherwise draw the expanded segment
                    {
                        DrawableSegment s = new DrawableSegment(segmentToProcess, radius);
                        // if next segment starts at the start or the middle of the current one, nothing will be connected to the end of the current segment - end cap is required.
                        connect(ref s, ref segmentToDraw, location, nextLocation == SegmentStartLocation.StartOrMiddle);
                        if (initialSkipped)
                            drawSegment(ref segmentToDraw);
                        initialSkipped = true;

                        segmentToDraw = s;
                        segmentToProcess = segments[i];
                        location = nextLocation;
                        nextLocation = SegmentStartLocation.End;
                    }
                }

                // Finish drawing last segment
                var ds = new DrawableSegment(segmentToProcess, radius);
                connect(ref ds, ref segmentToDraw, location, true);
                if (initialSkipped)
                    drawSegment(ref segmentToDraw);
                drawSegment(ref ds);
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);

                quadBatch?.Dispose();
            }

            private enum SegmentStartLocation
            {
                StartOrMiddle,
                End,
                Outside
            }

            private struct DrawableSegment
            {
                /// <summary>
                /// End point of this <see cref="DrawableSegment"/>.
                /// </summary>
                public readonly Vector2 EndPoint;

                /// <summary>
                /// Start point of this <see cref="DrawableSegment"/>.
                /// </summary>
                public readonly Vector2 StartPoint;

                /// <summary>
                /// The normalized direction of this <see cref="DrawableSegment"/>.
                /// </summary>
                public readonly Vector2 DirectionNormalized;

                /// <summary>
                /// The top-left position of the draw quad of this <see cref="DrawableSegment"/>.
                /// </summary>
                public Vector2 TopLeft;

                /// <summary>
                /// The top-right position of the draw quad of this <see cref="DrawableSegment"/>.
                /// </summary>
                public Vector2 TopRight;

                /// <summary>
                /// The bottom-left position of the draw quad of this <see cref="DrawableSegment"/>.
                /// </summary>
                public Vector2 BottomLeft;

                /// <summary>
                /// The bottom-right position of the draw quad of this <see cref="DrawableSegment"/>.
                /// </summary>
                public Vector2 BottomRight;

                /// <summary>
                /// The squared length of the line defining this <see cref="DrawableSegment"/>.
                /// </summary>
                public readonly float LengthSquared;

                /// <param name="guide">The line defining this <see cref="DrawableSegment"/>.</param>
                /// <param name="radius">The path radius.</param>
                public DrawableSegment(Line guide, float radius)
                {
                    StartPoint = guide.StartPoint;
                    EndPoint = guide.EndPoint;

                    Vector2 dir = guide.Direction;
                    LengthSquared = dir.X * dir.X + dir.Y * dir.Y;

                    if (LengthSquared < precision * precision)
                        dir = Vector2.UnitX;
                    else
                        dir /= MathF.Sqrt(LengthSquared);

                    DirectionNormalized = dir;

                    Vector2 ortho = new Vector2(-dir.Y, dir.X);

                    TopLeft = StartPoint + ortho * radius;
                    TopRight = EndPoint + ortho * radius;
                    BottomLeft = StartPoint - ortho * radius;
                    BottomRight = EndPoint - ortho * radius;
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            public readonly struct PathVertex : IEquatable<PathVertex>, IVertex
            {
                [VertexMember(2, VertexAttribPointerType.Float)]
                public readonly Vector2 Position;

                [VertexMember(2, VertexAttribPointerType.Float)]
                public readonly Vector2 StartPos;

                [VertexMember(2, VertexAttribPointerType.Float)]
                public readonly Vector2 EndPos;

                [VertexMember(1, VertexAttribPointerType.Float)]
                public readonly float Radius;

                public PathVertex(Vector2 position, Vector2 startPos, Vector2 endPos, float radius)
                {
                    Position = position;
                    StartPos = startPos;
                    EndPos = endPos;
                    Radius = radius;
                }

                public bool Equals(PathVertex other) =>
                    Position.Equals(other.Position)
                    && StartPos.Equals(other.StartPos)
                    && EndPos.Equals(other.EndPos)
                    && Radius.Equals(other.Radius);
            }
        }
    }
}
