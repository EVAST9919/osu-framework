#ifndef PATH_FS
#define PATH_FS

#extension GL_ARB_shader_storage_buffer_object : enable

#include "sh_Utils.h"
#include "sh_Masking.h"

struct PathNodeData
{
    int Left;
    int Right;
    bool IsLeaf;
    vec2 SegmentStart;
    vec2 SegmentEnd;
    vec4 Bounds;
};

#ifndef OSU_GRAPHICS_NO_SSBO

layout(std140, set = 2, binding = 0) readonly buffer g_PathBuffer
{
    PathNodeData Data[];
} PathBuffer;

#else // OSU_GRAPHICS_NO_SSBO

layout(std140, set = 2, binding = 0) uniform g_PathBuffer
{
    PathNodeData Data[64];
} PathBuffer;

#endif // OSU_GRAPHICS_NO_SSBO

layout(std140, set = 0, binding = 0) uniform m_PathData
{
    float PathRadius;
    vec4 VertexBounds;
};

layout(location = 2) in highp vec2 v_TexCoord;
layout(set = 1, binding = 0) uniform lowp texture2D m_Texture;
layout(set = 1, binding = 1) uniform lowp sampler m_Sampler;

layout(location = 0) out vec4 o_Colour;

highp float dstToLine(highp vec2 start, highp vec2 end, highp vec2 pixelPos)
{
    highp float lineLength = distance(end, start);

    if (lineLength < 0.001)
        return distance(pixelPos, start);

    highp vec2 a = (end - start) / lineLength;
    highp vec2 closest = clamp(dot(a, pixelPos - start), 0.0, distance(end, start)) * a + start; // closest point on a line from given position
    return distance(closest, pixelPos);
}

bool contains(vec2 pixelPos, int index)
{
    if (index == -1) return false;

    return pixelPos.x > PathBuffer.Data[index].Bounds.x && pixelPos.x < PathBuffer.Data[index].Bounds.z && pixelPos.y > PathBuffer.Data[index].Bounds.y && pixelPos.y < PathBuffer.Data[index].Bounds.w;
}

highp float dst(vec2 pixelPos)
{
    highp float m = 100.0;

    int stack[25];
    stack[0] = 0;
    int stackPointer = 0;

    while (stackPointer >= 0)
    {
        int index = stack[stackPointer];
        stackPointer--;
        
        if (!contains(pixelPos, index))
            continue;

        PathNodeData node = PathBuffer.Data[index];

        if (node.IsLeaf)
        {
            m = min(m, dstToLine(node.SegmentStart, node.SegmentEnd, pixelPos));
            continue;
        }
        else
        {
            stackPointer++;
            stack[stackPointer] = node.Left;

            stackPointer++;
            stack[stackPointer] = node.Right;
        }
    }

    return m;
}

void main(void)
{
    highp vec2 resolution = v_TexRect.zw - v_TexRect.xy;
    highp vec2 pixelPos = (v_TexCoord - v_TexRect.xy) / resolution; // from 0 to 1
    highp vec2 pixelPosReal = vec2(VertexBounds.x + (VertexBounds.z - VertexBounds.x) * pixelPos.x, VertexBounds.y + (VertexBounds.w - VertexBounds.y) * pixelPos.y);
    highp float d = dst(pixelPosReal);

    if (d > PathRadius)
    {
        o_Colour = vec4(0.0);
        return;
    }

    highp float p = d / PathRadius;

    o_Colour = getRoundedColor(texture(sampler2D(m_Texture, m_Sampler), v_TexRect.xy + vec2(1.0 - p, 0) * resolution, -0.9), v_TexCoord); // 1.0 - clamp(d, 0.0, PathRadius) / PathRadius);
}

#endif // SSBO_TEST_FS
