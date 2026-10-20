namespace ForgeLine.Presentation;

internal static class MaterialFilteringShader
{
    internal const string Source = """
        float PbrSpecularRoughness(float roughness, float3 dx, float3 dy) {
            // Preserve the qualified normal-variance response; alternative
            // kernels need a demonstrated material and temporal improvement.
            float variance=0.25*max(dot(dx,dx),dot(dy,dy));
            return sqrt(saturate(roughness*roughness+variance));
        }
        """;
}
