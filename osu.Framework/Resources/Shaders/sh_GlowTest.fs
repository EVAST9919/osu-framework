#ifndef GLOW_TEST_FS
#define GLOW_TEST_FS

#undef HIGH_PRECISION_VERTEX
#define HIGH_PRECISION_VERTEX

#undef PI
#define PI 3.1415926536

#include "sh_Utils.h"
#include "sh_Masking.h"

layout(location = 2) in highp vec2 v_TexCoord;

layout(std140, set = 0, binding = 0) uniform m_GlowTestParameters
{
    mediump float circleSize;
    mediump float glowSize;
    mediump float dstBetween;
    mediump float glowStrength;
    bool showOriginal;
};

layout(location = 0) out vec4 o_Colour;

highp float dstToCircle(highp vec2 pixelPos, highp vec2 circlePos, mediump float radius)
{        
    return distance(pixelPos, circlePos) - radius;
}

float smin(float a, float b, float k)
{
    if (k == 0.0)
        return min(a, b);

    float h = max(k - abs(a - b), 0.0) / k;
    return min(a, b) - h * h * k * (1.0 / 4.0);
}

highp float getGlow(highp float distance)
{
    if (glowSize == 0.0)
        return float(distance < 0.0);

    return smoothstep(glowSize * 0.5, - glowSize * 0.5, clamp(distance, - glowSize * 0.5, glowSize * 0.5));
}

lowp float getAlpha(highp vec2 pixelPos)
{
    float c1 = dstToCircle(pixelPos, vec2(0.5 - dstBetween * 0.5, 0.5), circleSize);
    float c2 = dstToCircle(pixelPos, vec2(0.5 + dstBetween * 0.5, 0.5), 0.1);

    float dst = smin(c1, c2, min(dstBetween * 0.2, glowSize * 0.5));
    lowp float objectsAlpha = float(min(c1, c2) < 0.0 && showOriginal);
    lowp float glowAlpha = getGlow(dst);

    highp float dstLines = dst;
    lowp float distanceBetweenLines = 0.05;
    lowp float lineThickness = 0.005;
    bool withinLine = false; //mod(dstLines, distanceBetweenLines) < lineThickness;

    float image = min(1.0, objectsAlpha + glowAlpha * glowStrength);

    return withinLine ? 1.0 - image : image;
}

void main(void)
{
    highp vec2 resolution = v_TexRect.zw - v_TexRect.xy;
    highp vec2 pixelPos = v_TexCoord / resolution;

    o_Colour = getRoundedColor(vec4(vec3(1.0), getAlpha(pixelPos)), v_TexCoord);
}

#endif