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
using osu.Framework.Graphics.Textures;
using osu.Framework.Utils;
using osuTK.Graphics;

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

            private Texture? texture;
            private float radius;
            private IShader? pathShader;
            private RectangleF texRect;

            private IVertexBatch<TexturedVertex3D>? triangleBatch;

            public PathDrawNode(Path source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                segments.Clear();
                segments.AddRange(Source.segments);

                texture = Source.Texture;
                texRect = texture.GetTextureRect(new RectangleF(0.5f, 0.5f, texture.Width - 1, texture.Height - 1));
                radius = Source.PathRadius;
                pathShader = Source.pathShader;
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (texture?.Available != true || segments.Count == 0 || pathShader == null || radius == 0f)
                    return;

                // Size must be divisible by 3 such that the amount of vertices is a multiple of the amount of vertices
                // per primitive (quads in this case). Otherwise overflowing the batch will result in wrong
                // grouping of vertices into primitives.
                triangleBatch ??= renderer.CreateLinearBatch<TexturedVertex3D>(9000, 10, PrimitiveTopology.Triangles);

                renderer.PushLocalMatrix(DrawInfo.Matrix);
                renderer.PushDepthInfo(DepthInfo.Default);

                // Blending is removed to allow for correct blending between the wedges of the path.
                renderer.SetBlend(BlendingParameters.None);

                pathShader.Bind();
                texture.Bind();

                updateVertexBuffer();

                pathShader.Unbind();

                renderer.PopDepthInfo();
                renderer.PopLocalMatrix();
            }

            /// <summary>
            /// Draws the provided segment to the screen.
            /// </summary>
            /// <param name="segment">The segment to be drawn.</param>
            /// <param name="prevSegment">Previous segment.</param>
            /// <param name="location">Position of the segment relative to the previous one.</param>
            /// <param name="endCap">Whether end cap of this segment must be drawn.</param>
            private void drawSegment(ref DrawableSegment segment, ref DrawableSegment prevSegment, SegmentStartLocation location, bool endCap)
            {
                // When segment starts outside the previous one, nothing is being connected to the start of the segment and start cap is required.
                bool startCap = location == SegmentStartLocation.Outside;

                Vector2 topLeft = segment.TopLeft;
                Vector2 topRight = segment.TopRight;
                Vector2 bottomLeft = segment.BottomLeft;
                Vector2 bottomRight = segment.BottomRight;
                Vector2 dir = segment.DirectionNormalized;

                // Segment starts at the end of the previous one
                if (location == SegmentStartLocation.End)
                {
                    Debug.Assert(prevSegment.EndPoint == segment.StartPoint);

                    Vector2 dir2 = -prevSegment.DirectionNormalized;

                    Vector2.Dot(ref dir, ref dir2, out float dot);
                    Vector2.PerpDot(ref dir, ref dir2, out float pDot);

                    // angle between segments is less than 90 degrees
                    if (dot >= 0)
                    {
                        float thetaDiff = MathF.Atan2(pDot, dot);
                        Line toConnect = thetaDiff < 0f ? new Line(prevSegment.TopRight, topLeft) : new Line(prevSegment.BottomRight, bottomLeft);
                        drawFan(segment.StartPoint, toConnect.StartPoint, toConnect.EndPoint, thetaDiff < 0f, (float)Math.PI - Math.Abs(thetaDiff));
                    }
                    else
                    {
                        float thetaDiff = Math.Abs(MathF.Atan(pDot / dot));

                        // at this small angle curvature isn't noticeable, we can get away with straight-up connecting segment to the previous one.
                        if (thetaDiff < Math.PI / max_res)
                        {
                            if (pDot < 0f)
                                topLeft = prevSegment.TopRight;
                            else
                                bottomLeft = prevSegment.BottomRight;
                        }
                        else
                        {
                            Line toConnect = pDot < 0f ? new Line(prevSegment.TopRight, topLeft) : new Line(prevSegment.BottomRight, bottomLeft);
                            drawFan(segment.StartPoint, toConnect.StartPoint, toConnect.EndPoint, pDot < 0f, thetaDiff);
                        }
                    }
                }

                if (startCap)
                    drawFan(segment.StartPoint, segment.TopLeft, segment.BottomLeft, false, MathF.PI);

                if (endCap)
                    drawFan(segment.EndPoint, segment.TopRight, segment.BottomRight, true, MathF.PI);

                createOuterVertex(topLeft);
                createOuterVertex(topRight);
                createInnerVertex(segment.StartPoint);

                createInnerVertex(segment.StartPoint);
                createOuterVertex(topRight);
                createInnerVertex(segment.EndPoint);

                createInnerVertex(segment.StartPoint);
                createInnerVertex(segment.EndPoint);
                createOuterVertex(bottomLeft);

                createOuterVertex(bottomLeft);
                createInnerVertex(segment.EndPoint);
                createOuterVertex(bottomRight);
            }

            private void createOuterVertex(Vector2 position)
            {
                Debug.Assert(triangleBatch != null);

                triangleBatch.Add(new TexturedVertex3D
                {
                    Position = new Vector3(position.X, position.Y, 0),
                    Colour = Color4.White,
                    TexturePosition = new Vector2(texRect.Left, texRect.Centre.Y)
                });
            }

            private void createInnerVertex(Vector2 position)
            {
                Debug.Assert(triangleBatch != null);

                triangleBatch.Add(new TexturedVertex3D
                {
                    Position = new Vector3(position.X, position.Y, 1),
                    Colour = Color4.White,
                    TexturePosition = new Vector2(texRect.Right, texRect.Centre.Y)
                });
            }

            private void drawFan(Vector2 origin, Vector2 start, Vector2 end, bool clockwise, float theta)
            {
                float step = (float)Math.PI / max_res * (clockwise ? -1 : 1);
                float angle = step;
                Vector2 lastPoint = start;

                while (Math.Abs(angle) < theta)
                {
                    Vector2 point = MathUtils.RotateAround(start, origin, angle);
                    drawFanTriangle(lastPoint, point, origin);
                    lastPoint = point;
                    angle += step;
                }

                drawFanTriangle(lastPoint, end, origin);
                return;

                void drawFanTriangle(Vector2 p1, Vector2 p2, Vector2 p3)
                {
                    createOuterVertex(p1);
                    createOuterVertex(p2);
                    createInnerVertex(p3);
                }
            }

            private void updateVertexBuffer()
            {
                Debug.Assert(segments.Count > 0);

                Line segmentToDraw = segments[0];

                SegmentStartLocation location = SegmentStartLocation.Outside;
                SegmentStartLocation nextLocation = SegmentStartLocation.End;

                // We initialize "fake" initial segment before the 0'th one
                // so that on first drawSegment() call with current SegmentStartLocation parameters path start cap will be drawn.
                DrawableSegment lastDrawnSegment = new DrawableSegment(segments[0], radius);

                for (int i = 1; i < segments.Count; i++)
                {
                    Vector2 dir = segmentToDraw.Direction;
                    float lengthSquared = dir.X * dir.X + dir.Y * dir.Y;
                    Vector2 nextVertex = segments[i].EndPoint;

                    // If segment is too short, make its end point equal start point of a new segment
                    if (lengthSquared < precision)
                    {
                        segmentToDraw = new Line(segmentToDraw.StartPoint, nextVertex);
                        continue;
                    }

                    Vector2 dir2 = nextVertex - segmentToDraw.StartPoint;
                    Vector2.PerpDot(ref dir, ref dir2, out float pDot);

                    // Expand segment if next end point is located within a line passing through it (distance from the next vertex to the segment is less than precision)
                    if (pDot * pDot / lengthSquared < precision * precision)
                    {
                        nextLocation = SegmentStartLocation.StartOrMiddle;

                        Vector2.Dot(ref dir, ref dir2, out float dot);

                        // new vertex is located behind the segment start point, expand segment backwards
                        if (dot < 0)
                        {
                            segmentToDraw = new Line(nextVertex, segmentToDraw.EndPoint);
                            location = SegmentStartLocation.Outside;
                        }
                        else if (dot > lengthSquared) // new vertex is located in front of the end point, expand segment forward
                        {
                            segmentToDraw = new Line(segmentToDraw.StartPoint, nextVertex);
                            nextLocation = SegmentStartLocation.End;
                        }
                    }
                    else // Otherwise draw the expanded segment
                    {
                        DrawableSegment s = new DrawableSegment(segmentToDraw, radius);
                        // if next segment starts at the start or the middle of the current one, nothing will be connected to the end of the current segment - end cap is required.
                        drawSegment(ref s, ref lastDrawnSegment, location, nextLocation == SegmentStartLocation.StartOrMiddle);

                        lastDrawnSegment = s;
                        segmentToDraw = segments[i];
                        location = nextLocation;
                        nextLocation = SegmentStartLocation.End;
                    }
                }

                // Finish drawing last segment
                var ds = new DrawableSegment(segmentToDraw, radius);
                drawSegment(ref ds, ref lastDrawnSegment, location, true);
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);

                triangleBatch?.Dispose();
            }

            private enum SegmentStartLocation
            {
                StartOrMiddle,
                End,
                Outside
            }

            private readonly struct DrawableSegment
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
                public readonly Vector2 TopLeft;

                /// <summary>
                /// The top-right position of the draw quad of this <see cref="DrawableSegment"/>.
                /// </summary>
                public readonly Vector2 TopRight;

                /// <summary>
                /// The bottom-left position of the draw quad of this <see cref="DrawableSegment"/>.
                /// </summary>
                public readonly Vector2 BottomLeft;

                /// <summary>
                /// The bottom-right position of the draw quad of this <see cref="DrawableSegment"/>.
                /// </summary>
                public readonly Vector2 BottomRight;

                /// <param name="guide">The line defining this <see cref="DrawableSegment"/>.</param>
                /// <param name="radius">The path radius.</param>
                public DrawableSegment(Line guide, float radius)
                {
                    StartPoint = guide.StartPoint;
                    EndPoint = guide.EndPoint;

                    Vector2 dir = guide.Direction;
                    float lengthSquared = dir.X * dir.X + dir.Y * dir.Y;

                    if (lengthSquared < precision * precision)
                        dir = Vector2.UnitX;
                    else
                        dir /= MathF.Sqrt(lengthSquared);

                    DirectionNormalized = dir;

                    Vector2 ortho = new Vector2(-dir.Y, dir.X);

                    TopLeft = StartPoint + ortho * radius;
                    TopRight = EndPoint + ortho * radius;
                    BottomLeft = StartPoint - ortho * radius;
                    BottomRight = EndPoint - ortho * radius;
                }
            }
        }
    }
}
