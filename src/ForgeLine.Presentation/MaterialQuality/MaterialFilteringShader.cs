namespace ForgeLine.Presentation;

internal static class MaterialFilteringShader
{
    internal const string Source = """
        float PbrSpecularRoughness(float roughness, float3 dx, float3 dy) {
            // Accumulate the two screen axes within the existing roughness
            // domain. Bound the footprint so seams retain material identity.
            float variance = min(0.25 * (dot(dx,dx) + dot(dy,dy)), 0.04);
            return sqrt(saturate(roughness * roughness + variance));
        }
        """;
}
