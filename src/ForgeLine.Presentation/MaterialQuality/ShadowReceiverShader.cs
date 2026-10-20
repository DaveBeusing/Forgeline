namespace ForgeLine.Presentation;

internal static class ShadowReceiverShader
{
    internal const string Source = """
        float2 ShadowReceiverPlane(float2 du,float2 dv,float dzdx,float dzdy) {
            float determinant=du.x*dv.y-du.y*dv.x;
            float footprint=dot(du,du)*dot(dv,dv);
            // Relative conditioning keeps thin receivers independent of zoom.
            float2 plane=determinant*determinant>max(1e-30,footprint*1e-6) ?
                float2(dzdx*dv.y-dzdy*du.y,dzdy*du.x-dzdx*dv.x)/determinant : 0;
            return all(isfinite(plane)) ? plane : 0;
        }
        float ShadowReceiverDepth(float depth,float2 plane,float2 offset,float maxCorrection) {
            return depth+clamp(dot(plane,offset),-maxCorrection,maxCorrection);
        }
        """;
}
