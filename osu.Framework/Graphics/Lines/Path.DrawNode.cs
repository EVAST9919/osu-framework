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
using osu.Framework.Graphics.Shaders.Types;
using osuTK.Graphics.ES30;

namespace osu.Framework.Graphics.Lines
{
    public partial class Path
    {
        private class PathDrawNode : DrawNode
        {
            private const float precision = 0.01f; // Smallest allowed segment length. Used for segment reduction algorithm.

            protected new Path Source => (Path)base.Source;

            private readonly List<Line> segments = new List<Line>();

            private float radius;
            private IShader? pathShader;
            private IUniformBuffer<PathParameters>? parametersBuffer;

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

                parametersBuffer ??= renderer.CreateUniformBuffer<PathParameters>();
                parametersBuffer.Data = new PathParameters
                {
                    Radius = radius,
                };
                pathShader.BindUniformBlock("m_PathParameters", parametersBuffer);

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
                Vector2 dir = segment.DirectionNormalized;
                Vector2 capOffset = dir * radius;

                if (endCap)
                {
                    segment.TopRight += capOffset;
                    segment.BottomRight += capOffset;
                }

                // When segment starts outside the previous one, nothing is being connected to the start of the segment and start cap is required.
                if (location == SegmentStartLocation.Outside)
                {
                    segment.TopLeft -= capOffset;
                    segment.BottomLeft -= capOffset;
                }
                else if (location == SegmentStartLocation.End) // Segment starts at the end of the previous one - figure out the connection type
                {
                    Debug.Assert(prevSegment.EndPoint == segment.StartPoint);

                    Vector2 dir2 = -prevSegment.DirectionNormalized;
                    Vector2.Dot(ref dir, ref dir2, out float dot);

                    // Angle between segments is less than 90 degrees - use segment start cap.
                    // We can't connect segments directly with a sharp angle, so we are using segment start cap to cover the hole.
                    if (dot >= 0)
                    {
                        segment.TopLeft -= capOffset;
                        segment.BottomLeft -= capOffset;
                        return;
                    }

                    // angle is more than 90 degrees - connect segments to each other
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

            private void drawSegment(ref DrawableSegment s)
            {
                Debug.Assert(quadBatch != null);

                quadBatch.Add(new PathVertex(s.TopLeft, s.StartPoint, s.EndPoint));
                quadBatch.Add(new PathVertex(s.TopRight, s.StartPoint, s.EndPoint));
                quadBatch.Add(new PathVertex(s.BottomRight, s.StartPoint, s.EndPoint));
                quadBatch.Add(new PathVertex(s.BottomLeft, s.StartPoint, s.EndPoint));
            }

            private void updateVertexBuffer()
            {
                Debug.Assert(segments.Count > 0);

                Line mergedSegment = segments[0];

                // location of the current segment relative to the previous one.
                SegmentStartLocation location = SegmentStartLocation.Outside;
                SegmentStartLocation nextLocation = SegmentStartLocation.End;

                // We initialize "fake" initial segment before the 0'th one (which we don't want to draw)
                // so that on first connect() call with current SegmentStartLocation parameters path start cap will be added.
                DrawableSegment prevSegment = new DrawableSegment(mergedSegment, radius);
                bool drawPrevious = false;

                // This loop tries to merge consecutive path segments which are located within the same line.
                // If next segment deviates from the one being processed we will save it for drawing later, after the new one will be fully processed
                // since the connection type between segments can be established only after we know "final" shape of them both.
                for (int i = 1; i < segments.Count; i++)
                {
                    Line nextSegment = segments[i];

                    Vector2 dir = mergedSegment.Direction;
                    float mergedLengthSquared = dir.X * dir.X + dir.Y * dir.Y;
                    Vector2 nextVertex = nextSegment.EndPoint;

                    // If merged segment is too short, connect it to the next vertex directly.
                    if (mergedLengthSquared < precision * precision)
                    {
                        mergedSegment = new Line(mergedSegment.StartPoint, nextVertex);
                        continue;
                    }

                    Vector2 dir2 = nextVertex - mergedSegment.StartPoint;
                    Vector2.PerpDot(ref dir, ref dir2, out float pDot);

                    // Expand segment if next vertex is located within a line passing through it (distance from the next vertex to the segment is less than precision)
                    if (pDot * pDot / mergedLengthSquared < precision * precision)
                    {
                        nextLocation = SegmentStartLocation.StartOrMiddle;

                        Vector2.Dot(ref dir, ref dir2, out float dot);

                        // new vertex is located behind the segment start point, expand segment backwards
                        if (dot < 0)
                        {
                            mergedSegment = new Line(nextVertex, mergedSegment.EndPoint);
                            location = SegmentStartLocation.Outside;
                        }
                        else if (dot > mergedLengthSquared) // new vertex is located in front of the end point, expand segment forward
                        {
                            mergedSegment = new Line(mergedSegment.StartPoint, nextVertex);
                            nextLocation = SegmentStartLocation.End;
                        }
                    }
                    else // Otherwise draw expanded segment
                    {
                        DrawableSegment s = new DrawableSegment(mergedSegment, radius);
                        // if next segment starts at the start or the middle of the current one, nothing will be connected to the end of the current segment - end cap is required.
                        connect(ref s, ref prevSegment, location, nextLocation == SegmentStartLocation.StartOrMiddle);

                        if (drawPrevious)
                            drawSegment(ref prevSegment);
                        drawPrevious = true;

                        prevSegment = s;
                        mergedSegment = nextSegment;
                        location = nextLocation;
                        nextLocation = SegmentStartLocation.End;
                    }
                }

                // Finish drawing last segments
                var ds = new DrawableSegment(mergedSegment, radius);
                connect(ref ds, ref prevSegment, location, true);

                if (drawPrevious)
                    drawSegment(ref prevSegment);

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

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            private record struct PathParameters
            {
                public UniformFloat Radius;
                private UniformPadding12 pad;
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

                public PathVertex(Vector2 position, Vector2 startPos, Vector2 endPos)
                {
                    Position = position;
                    StartPos = startPos;
                    EndPos = endPos;
                }

                public bool Equals(PathVertex other) =>
                    Position.Equals(other.Position)
                    && StartPos.Equals(other.StartPos)
                    && EndPos.Equals(other.EndPos);
            }
        }
    }
}
