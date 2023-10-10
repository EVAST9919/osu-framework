#ifndef CIRCULAR_PROGRESS_EDGE_EFFECT_FS
#define CIRCULAR_PROGRESS_EDGE_EFFECT_FS

#undef HIGH_PRECISION_VERTEX
#define HIGH_PRECISION_VERTEX

#include "sh_Utils.h"
#include "sh_Masking.h"
#include "sh_CircularProgressUtils.h"

layout(location = 2) in highp vec2 v_TexCoord;

layout(std140, set = 0, binding = 0) uniform m_CircularProgressEdgeEffectParameters
{
    highp vec2 glowSize;
    mediump float innerRadius;
    mediump float progress;
    highp float texelSize;
    highp float roundness;
    bool roundedCaps;
    bool hollow;
};

layout(location = 0) out vec4 o_Colour;

lowp float getGlow(highp float distance, highp float size)
{
    // Many sources suggest that y = 1/x function looks the best for glow and imo it really does.
    // We will pick a part of it with x ranging from 0.4 to 5.0, but these values can be adjusted.

    const float lowerX = 0.4;
    const float higherX = 5.0;

    highp float ratio = clamp(distance, 0.0, size) / size; // how far away we are from the object within glow (from 0 to 1)
    highp float x = lowerX + ratio * (higherX - lowerX);
    highp float glow = 1.0 / x * lowerX; // adjust to be within 0..1 range
    return glow * (1.0 - ratio); // function won't reach 0, add linear fade on top
}

lowp float getBlur(highp float distance, highp float size)
{
    return smoothstep(size, -size, distance);
}

lowp float getGlowDebug(highp float distance, highp float size)
{
    if (distance < 0.0)
        return 1.0;

    if (distance < size)
        return 0.5;

    return 0.0;
}

bool isLeft(vec2 a, vec2 b, vec2 c)
{
    return (b.x - a.x)*(c.y - a.y) - (b.y - a.y)*(c.x - a.x) > 0.0;
}

float smin(float a, float b, float k)
{
    if (k == 0.0)
        return min(a, b);

    float h = max(k - abs(a - b), 0.0) / k;
    return min(a, b) - h * h * k * 0.25;
}

highp float dstToRoundedProgress(highp vec2 pixelPos, mediump float progress, mediump float innerRadius, bool roundedCaps, highp float texelSize)
{
    if (roundness == 0.0 || roundedCaps)
        return distanceToProgress(pixelPos, progress, innerRadius, roundedCaps, texelSize);

    // Compute angle of the current pixel in the (0, 2*PI) range
    mediump float pixelAngle = atan(0.5 - pixelPos.y, 0.5 - pixelPos.x) - HALF_PI;
    if (pixelAngle < 0.0)
        pixelAngle += TWO_PI;

    mediump float progressAngle = TWO_PI * progress;
    mediump float pathRadius = 0.25 * innerRadius;
    highp float halfTexel = texelSize * 0.5;
    float alpha = progressAngle * 0.5;

    highp float shrinkage = min(roundness * 0.125, pathRadius);

    if (progress < 0.5)
    {
        float minWidth = sin(alpha * 0.5) * sin((PI - alpha) * 0.5) / (sin((HALF_PI + alpha) * 0.5) * 2.0 * cos(HALF_PI - (HALF_PI + alpha) * 0.5));
        shrinkage = min(shrinkage, minWidth * 0.5);
    }

    highp float idleTopAngle = asin(shrinkage / (0.5 - shrinkage));

    if (pixelAngle > idleTopAngle && pixelAngle < progressAngle - idleTopAngle && distance(pixelPos, vec2(0.5)) > 0.5 - shrinkage)
        return abs(distance(pixelPos, vec2(0.5)) - (0.5 - pathRadius)) - pathRadius;

    highp float idleTopLength = sqrt(0.25 - shrinkage);
    highp vec2 idleEdgeTop = vec2(0.5 + shrinkage, 0.5 - idleTopLength);

    highp float idleBottomAngle = innerRadius == 1.0 ? asin(1.0) : asin(shrinkage / (0.5 + shrinkage - 0.5 * innerRadius));

    highp vec2 csTop = vec2(cos(progressAngle - idleTopAngle - HALF_PI), sin(progressAngle - idleTopAngle - HALF_PI));
    highp vec2 rotatingEdgeTop = vec2(0.5) + csTop * vec2(0.5 - shrinkage);

    if (idleBottomAngle > progressAngle - idleBottomAngle)
    {
        float length = shrinkage / sin(alpha);

        highp vec2 cs = vec2(cos(alpha - HALF_PI), sin(alpha - HALF_PI));
        highp vec2 bottomEdge = vec2(0.5) + cs * vec2(length);

        return min(dstToLine(idleEdgeTop, bottomEdge, pixelPos), dstToLine(rotatingEdgeTop, bottomEdge, pixelPos)) - shrinkage;
    }

    if (pixelAngle > idleBottomAngle && pixelAngle < progressAngle - idleBottomAngle && distance(pixelPos, vec2(0.5)) < 0.5 + shrinkage - 0.5 * innerRadius)
        return abs(distance(pixelPos, vec2(0.5)) - (0.5 - pathRadius)) - pathRadius;

    highp float idleBottomLength = innerRadius == 1.0 ? 0.0 : sqrt((0.5 + shrinkage - 0.5 * innerRadius) * (0.5 + shrinkage - 0.5 * innerRadius) - shrinkage * shrinkage);
    highp vec2 idleEdgeBottom = vec2(0.5 + shrinkage, 0.5 - idleBottomLength);
    highp vec2 csBottom = vec2(cos(progressAngle - idleBottomAngle - HALF_PI), sin(progressAngle - idleBottomAngle - HALF_PI));
    highp vec2 rotatingEdgeBottom = vec2(0.5) + csBottom * vec2(0.5 + shrinkage - 0.5 * innerRadius);

    return min(dstToLine(idleEdgeTop, idleEdgeBottom, pixelPos), dstToLine(rotatingEdgeTop, rotatingEdgeBottom, pixelPos)) - shrinkage;
}

lowp float progresGlow(highp vec2 pixelPos, mediump float progress, mediump float innerRadius, bool roundedCaps, highp float texelSize)
{
    // Compute angle of the current pixel in the (0, 2*PI) range
    mediump float pixelAngle = atan(0.5 - pixelPos.y, 0.5 - pixelPos.x) - HALF_PI;
    if (pixelAngle < 0.0)
        pixelAngle += TWO_PI;

    mediump float progressAngle = TWO_PI * progress;
    mediump float pathRadius = 0.25 * innerRadius;
    highp float halfTexel = texelSize * 0.5;

    if (progress >= 1.0 || pixelAngle < progressAngle) // Pixel inside the sector
        return getGlow(abs(distance(pixelPos, vec2(0.5)) - (0.5 - pathRadius - halfTexel)) - pathRadius + halfTexel, min(glowSize.x, glowSize.y));

    highp vec2 cs = vec2(cos(progressAngle - HALF_PI), sin(progressAngle - HALF_PI));

    highp vec2 rotatingEdgeTop = vec2(0.5) + cs * vec2(0.5 - texelSize);
    highp vec2 rotatingEdgeBottom = vec2(0.5) + cs * vec2(0.5 - 2.0 * pathRadius);

    if (roundedCaps) // Pixel outside the sector with rounded caps enabled
    {
        highp vec2 arcStart = vec2(0.5, pathRadius + halfTexel);
        highp vec2 arcEnd = vec2(0.5) + cs * vec2(0.5 - pathRadius - halfTexel);

        highp float dstToIdle = distance(pixelPos, arcStart) + halfTexel - pathRadius;
        highp float dstToRotating = distance(pixelPos, arcEnd) + halfTexel - pathRadius;

        lowp float glowIdle = getGlow(dstToIdle, min(glowSize.x, glowSize.y));
        lowp float glowRotating = getGlow(dstToRotating, min(glowSize.x, glowSize.y));

        if (pixelPos.x < 0.5 && isLeft(rotatingEdgeBottom, rotatingEdgeTop, pixelPos))
        {
            return (glowRotating + glowIdle);
            //float blobsDst = smin(dstToIdle, dstToRotating, min(distance(arcStart, arcEnd) * 0.2, min(glowSize.x, glowSize.y) * 0.2));
            //return getGlow(blobsDst, min(glowSize.x, glowSize.y));
        }

        return max(glowIdle, glowRotating);
    }

    return 0.0;

    highp float dstToIdleEdge = dstToLine(vec2(0.5, texelSize), vec2(0.5, 2.0 * pathRadius), pixelPos);
    highp float dstToRotatingEdge = dstToLine(rotatingEdgeTop, rotatingEdgeBottom, pixelPos);

    return min(dstToIdleEdge, dstToRotatingEdge);
}

void main(void)
{
    highp vec2 resolution = v_TexRect.zw - v_TexRect.xy;

    // Inflate coordinate space, so it would be (-glowSize -> 0 -> 1 -> glowSize) to preserve everything in place while inflating the draw quad
    highp vec2 pixelPos = (v_TexCoord / resolution) * (vec2(1.0) + glowSize * 2.0) - glowSize;

    highp float dst = dstToRoundedProgress(pixelPos, progress, innerRadius, roundedCaps, texelSize);
    lowp float glowA = hollow && dst < 0.0 ? smoothstep(texelSize, 0.0, -dst) : getGlow(dst, min(glowSize.x, glowSize.y));
    //lowp float glowA = smoothstep(texelSize, 0.0, dst);
    o_Colour = getRoundedColor(vec4(vec3(1.0), glowA), v_TexCoord);
}

#endif