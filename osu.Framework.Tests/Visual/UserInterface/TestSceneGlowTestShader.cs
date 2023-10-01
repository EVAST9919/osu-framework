// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Runtime.InteropServices;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Shaders.Types;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;

namespace osu.Framework.Tests.Visual.UserInterface
{
    public partial class TestSceneGlowTestShader : FrameworkTestScene
    {
        private readonly GlowShader shader;
        private readonly BufferedContainer buffered;

        public TestSceneGlowTestShader()
        {
            AddRange(new Drawable[]
            {
                buffered = new BufferedContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = shader = new GlowShader
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(500)
                    }
                }
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            AddSliderStep("Glow size", 0f, 1f, 0f, g => shader.GlowSize = g);
            AddSliderStep("Glow strength", 0f, 5f, 1f, g => shader.GlowStrength = g);
            AddSliderStep("Circle size", 0f, 1f, 0.1f, c => shader.CircleSize = c);
            AddSliderStep("Distance between", 0f, 0.5f, 0.1f, d => shader.DstBetween = d);
            AddToggleStep("Show original", s => shader.ShowOriginal = s);

            AddSliderStep("Buffered glow size", 0f, 100f, 0f, g => buffered.BlurSigma = new Vector2(g));
            AddToggleStep("Buffered show original", s => buffered.DrawOriginal = s);
        }

        private partial class GlowShader : Box
        {
            private float glowSize;

            public float GlowSize
            {
                get => glowSize;
                set
                {
                    glowSize = value;
                    Invalidate(Invalidation.DrawNode);
                }
            }

            private float glowStrength = 1f;

            public float GlowStrength
            {
                get => glowStrength;
                set
                {
                    glowStrength = value;
                    Invalidate(Invalidation.DrawNode);
                }
            }

            private float dstBetween;

            public float DstBetween
            {
                get => dstBetween;
                set
                {
                    dstBetween = value;
                    Invalidate(Invalidation.DrawNode);
                }
            }

            private float circleSize;

            public float CircleSize
            {
                get => circleSize;
                set
                {
                    circleSize = value;
                    Invalidate(Invalidation.DrawNode);
                }
            }

            private bool showOriginal;

            public bool ShowOriginal
            {
                get => showOriginal;
                set
                {
                    showOriginal = value;
                    Invalidate(Invalidation.DrawNode);
                }
            }

            [BackgroundDependencyLoader]
            private void load(ShaderManager shaders)
            {
                TextureShader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, "GlowTest");
            }

            protected override DrawNode CreateDrawNode() => new GlowShaderDrawNode(this);

            private class GlowShaderDrawNode : SpriteDrawNode
            {
                public new GlowShader Source => (GlowShader)base.Source;

                public GlowShaderDrawNode(GlowShader source)
                    : base(source)
                {
                }

                private float glowSize;
                private float circleSize;
                private float dstBetween;
                private float glowStrength;
                private bool showOriginal;

                public override void ApplyState()
                {
                    base.ApplyState();

                    glowSize = Source.glowSize;
                    glowStrength = Source.glowStrength;
                    circleSize = Source.circleSize;
                    dstBetween = Source.dstBetween;
                    showOriginal = Source.showOriginal;
                }

                private IUniformBuffer<GlowTestParameters> parametersBuffer;

                protected override void BindUniformResources(IShader shader, IRenderer renderer)
                {
                    base.BindUniformResources(shader, renderer);

                    parametersBuffer ??= renderer.CreateUniformBuffer<GlowTestParameters>();
                    parametersBuffer.Data = new GlowTestParameters
                    {
                        CircleSize = circleSize,
                        GlowSize = glowSize,
                        DstBetween = dstBetween,
                        GlowStrength = glowStrength,
                        ShowOriginal = showOriginal,
                    };

                    shader.BindUniformBlock("m_GlowTestParameters", parametersBuffer);
                }

                protected internal override bool CanDrawOpaqueInterior => false;

                protected override void Dispose(bool isDisposing)
                {
                    base.Dispose(isDisposing);
                    parametersBuffer?.Dispose();
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                private record struct GlowTestParameters
                {
                    public UniformFloat CircleSize;
                    public UniformFloat GlowSize;
                    public UniformFloat DstBetween;
                    public UniformFloat GlowStrength;
                    public UniformBool ShowOriginal;
                    private UniformPadding12 pad;
                }
            }
        }
    }
}
