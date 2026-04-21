// 文件名: CustomTessellation.hlsl
// 版本: 1.0
// 日期: 2026-04-21
// 说明: URP 草地几何着色器的曲面细分模块（Hull/Domain Shader）。提供均匀三角形细分以控制草叶密度。
// 管线: 通用渲染管线 (URP) - HLSL
// =============================================================================

struct vertexInput
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float4 tangent : TANGENT;
    float2 uv : TEXCOORD0;
};

struct vertexOutput
{
    float4 vertex : SV_POSITION;
    float3 normal : NORMAL;
    float4 tangent : TANGENT;
    float2 uv : TEXCOORD0;
};

struct TessellationFactors
{
    float edge[3] : SV_TessFactor;
    float inside : SV_InsideTessFactor;
};

vertexInput vert(vertexInput v)
{
    return v;
}

vertexOutput tessVert(vertexInput v)
{
    vertexOutput o;
    o.vertex = v.vertex;
    o.normal = v.normal;
    o.tangent = v.tangent;
    o.uv = v.uv;
    return o;
}

// _TessellationUniform is declared in the UnityPerMaterial CBUFFER in Grass.shader

TessellationFactors patchConstantFunction(InputPatch<vertexInput, 3> patch)
{
    TessellationFactors f;
    f.edge[0] = _TessellationUniform;
    f.edge[1] = _TessellationUniform;
    f.edge[2] = _TessellationUniform;
    f.inside = _TessellationUniform;
    return f;
}

[domain("tri")]
[outputcontrolpoints(3)]
[outputtopology("triangle_cw")]
[partitioning("integer")]
[patchconstantfunc("patchConstantFunction")]
vertexInput hull(InputPatch<vertexInput, 3> patch, uint id : SV_OutputControlPointID)
{
    return patch[id];
}

[domain("tri")]
vertexOutput domain(TessellationFactors factors, OutputPatch<vertexInput, 3> patch, float3 barycentricCoordinates : SV_DomainLocation)
{
    vertexInput v;

    #define MY_DOMAIN_PROGRAM_INTERPOLATE(fieldName) v.fieldName = \
        patch[0].fieldName * barycentricCoordinates.x + \
        patch[1].fieldName * barycentricCoordinates.y + \
        patch[2].fieldName * barycentricCoordinates.z;

    MY_DOMAIN_PROGRAM_INTERPOLATE(vertex)
    MY_DOMAIN_PROGRAM_INTERPOLATE(normal)
    MY_DOMAIN_PROGRAM_INTERPOLATE(tangent)
    MY_DOMAIN_PROGRAM_INTERPOLATE(uv)

    // 归一化插值后的法线和切线方向，防止矩阵退化导致草根部拉扯
    v.normal = normalize(v.normal);
    v.tangent.xyz = normalize(v.tangent.xyz);
    // tangent.w 是副法线符号（+1/-1），不应被插值，取第一个顶点的值
    v.tangent.w = patch[0].tangent.w;

    return tessVert(v);
}
