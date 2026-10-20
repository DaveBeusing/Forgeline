namespace ForgeLine.Presentation;

internal static class MaterialFilteringShader
{
    internal const string Source = """
        float PbrSpecularRoughness(float roughness, float3 dx, float3 dy) {
            // Filter the GGX squared slope, not perceptual roughness. Bound the
            // footprint contribution so seams cannot erase material identity.
            float variance = min(0.25 * (dot(dx,dx) + dot(dy,dy)), 0.04);
            float alpha = roughness * roughness;
            return sqrt(sqrt(saturate(alpha * alpha + variance)));
        }
        """;
}
