#ifndef CIRCULAR_PROGRESS_UTILS_H
#define CIRCULAR_PROGRESS_UTILS_H

#undef PI
#define PI 3.1415926536

#undef HALF_PI
#define HALF_PI 1.57079632679

#undef TWO_PI
#define TWO_PI 6.28318530718

highp float dstToLine(highp vec2 start, highp vec2 end, highp vec2 pixelPos)
{
    highp float lineLength = distance(end, start);

    if (lineLength < 0.001)
        return distance(pixelPos, start);

    highp vec2 a = (end - start) / lineLength;
    highp vec2 closest = clamp(dot(a, pixelPos - start), 0.0, distance(end, start)) * a + start; // closest point on a line from given position
    return distance(closest, pixelPos);
}

// Returns distance to the progress shape (to closest pixel on it's border)
highp float distanceToProgress(highp vec2 pixelPos, mediump float progress, mediump float innerRadius, bool roundedCaps, highp float texelSize)
{
    // Compute angle of the current pixel in the (0, 2*PI) range
    mediump float pixelAngle = atan(0.5 - pixelPos.y, 0.5 - pixelPos.x) - HALF_PI;
    if (pixelAngle < 0.0)
        pixelAngle += TWO_PI;

    mediump float progressAngle = TWO_PI * progress;
    mediump float pathRadius = 0.25 * innerRadius;
    highp float halfTexel = texelSize * 0.5;

    if (progress >= 1.0 || pixelAngle < progressAngle) // Pixel inside the sector
        return abs(distance(pixelPos, vec2(0.5)) - (0.5 - pathRadius - halfTexel)) - pathRadius + halfTexel;

    highp vec2 cs = vec2(cos(progressAngle - HALF_PI), sin(progressAngle - HALF_PI));

    if (roundedCaps) // Pixel outside the sector with rounded caps enabled
    {
        highp vec2 arcStart = vec2(0.5, pathRadius + halfTexel);
        highp vec2 arcEnd = vec2(0.5) + cs * vec2(0.5 - pathRadius - halfTexel);

        return min(distance(pixelPos, arcStart), distance(pixelPos, arcEnd)) + halfTexel - pathRadius;
    }

    highp float dstToIdleEdge = dstToLine(vec2(0.5, texelSize), vec2(0.5, 2.0 * pathRadius), pixelPos);

    highp vec2 rotatingEdgeTop = vec2(0.5) + cs * vec2(0.5 - texelSize);
    highp vec2 rotatingEdgeBottom = vec2(0.5) + cs * vec2(0.5 - 2.0 * pathRadius);
    highp float dstToRotatingEdge = dstToLine(rotatingEdgeTop, rotatingEdgeBottom, pixelPos);

    return min(dstToIdleEdge, dstToRotatingEdge);
}

bool isLeft(highp vec2 a, highp vec2 b, highp vec2 c)
{
    return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) > 0.0;
}

highp float distanceToRoundedProgress(highp vec2 pixelPos, mediump float progress, mediump float innerRadius, bool roundedCaps, highp float texelSize, mediump float roundness)
{
    if (roundness == 0.0 || roundedCaps)
        return distanceToProgress(pixelPos, progress, innerRadius, roundedCaps, texelSize);

    // Compute angle of the current pixel in the (0, 2*PI) range
    mediump float pixelAngle = atan(0.5 - pixelPos.y, 0.5 - pixelPos.x) - HALF_PI;
    if (pixelAngle < 0.0)
        pixelAngle += TWO_PI;

    mediump float progressAngle = TWO_PI * progress;
    mediump float pathRadius = 0.25 * innerRadius;
    
    highp float topShrinkage = min(roundness * 0.125, pathRadius - texelSize * 0.5);
    highp float topAngle = asin(topShrinkage / (0.5 - texelSize - topShrinkage));
    topAngle = min(topAngle, progressAngle * 0.5);

    if (pixelAngle < topAngle || pixelAngle > progressAngle - topAngle)
    {
        topShrinkage = sin(topAngle) * (0.5 - texelSize) / (1.0 + sin(topAngle));
        
        highp vec2 idleEdgeTop = vec2(0.5 + topShrinkage, 0.5 - sqrt(0.25 + texelSize * texelSize - texelSize - topShrinkage + 2.0 * topShrinkage * texelSize));
        highp vec2 csTop = vec2(cos(progressAngle - topAngle - HALF_PI), sin(progressAngle - topAngle - HALF_PI));
        highp vec2 rotatingEdgeTop = vec2(0.5) + csTop * vec2(0.5 - texelSize - topShrinkage);
        
        if (pixelPos.y < idleEdgeTop.y)
            return min(distance(pixelPos, idleEdgeTop), distance(pixelPos, rotatingEdgeTop)) - topShrinkage;
        
        highp float topDir = TWO_PI - progressAngle - HALF_PI;
        highp vec2 topSegmentEnd = rotatingEdgeTop + 0.5 * vec2(sin(topDir), cos(topDir));
        if (isLeft(rotatingEdgeTop, topSegmentEnd, pixelPos))
            return min(distance(pixelPos, idleEdgeTop), distance(pixelPos, rotatingEdgeTop)) - topShrinkage;
    }

    if (innerRadius < 1.0)
    {
        highp float bottomShrinkage = min(roundness * 0.125, pathRadius - texelSize * 0.5);
        highp float bottomAngle = asin(bottomShrinkage / (0.5 + bottomShrinkage - 0.5 * innerRadius));
        bottomAngle = min(bottomAngle, topAngle);

        if (pixelAngle < bottomAngle || pixelAngle > progressAngle - bottomAngle)
        {
            bottomShrinkage = sin(bottomAngle) * (0.5 - 0.5 * innerRadius) / (1.0 - sin(bottomAngle));

            highp vec2 idleEdgeBottom = vec2(0.5 + bottomShrinkage, (0.5 * tan(bottomAngle) - bottomShrinkage) / tan(bottomAngle));
            highp vec2 csBottom = vec2(cos(progressAngle - bottomAngle - HALF_PI), sin(progressAngle - bottomAngle - HALF_PI));
            highp vec2 rotatingEdgeBottom = vec2(0.5) + csBottom * vec2(0.5 + bottomShrinkage - 0.5 * innerRadius);
            
            highp float bottomDir = TWO_PI - progressAngle - HALF_PI;
            highp vec2 bottomSegmentEnd = rotatingEdgeBottom + 0.5 * vec2(sin(bottomDir), cos(bottomDir));
            if (!isLeft(rotatingEdgeBottom, bottomSegmentEnd, pixelPos) && pixelPos.y > idleEdgeBottom.y)
                return min(distance(pixelPos, idleEdgeBottom), distance(pixelPos, rotatingEdgeBottom)) - bottomShrinkage;
        }
    }

    return distanceToProgress(pixelPos, progress, innerRadius, roundedCaps, texelSize);
}

lowp float progressAlphaAt(highp vec2 pixelPos, mediump float progress, mediump float innerRadius, bool roundedCaps, highp float texelSize, mediump float roundness)
{
    // This is a bit of a hack to make progress appear smooth if it's radius < texelSize by making it more transparent while leaving thickness the same
    lowp float subAAMultiplier = clamp(innerRadius / (texelSize * 2.0), 0.1, 1.0);
    innerRadius = max(innerRadius, texelSize * 2.0);
    
    return smoothstep(texelSize, 0.0, distanceToRoundedProgress(pixelPos, progress, innerRadius, roundedCaps, texelSize, roundness)) * subAAMultiplier;
}

#endif
