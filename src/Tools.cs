using Raylib_cs;
using System.Numerics;

namespace RaylibErosionStandalone;

internal static class Tools
{
    static Random rnd = new();

    public static float randomRange(float min, float max)
    {
        //TODO check equivalence. We should use same Random object as for ErosionMaker if we want to stick to original code
        return min + rnd.Next(0, 32767) / ((float)32767 / (max - min));
        //return min + static_cast <float> (rand()) / (static_cast <float> (RAND_MAX / (max - min)));
    }

    public static float Lerp(float firstFloat, float secondFloat, float by)
    {
        return firstFloat * (1 - by) + secondFloat * by;
    }

    // Generate cube map texture from HDR texture
    public static unsafe Texture2D GenTextureCubeMap(Shader shader, Texture2D panorama, int size,
        PixelFormat format)
    {
        Texture2D cubeMap;

        // Disable backface culling to render inside the cube
        Rlgl.DisableBackfaceCulling();

        // STEP 1: Setup framebuffer
        //------------------------------------------------------------------------------------------
        var rbo = Rlgl.LoadTextureDepth(size, size, true);
        cubeMap.Id = Rlgl.LoadTextureCubemap(null, size, format, 1);

        var fbo = Rlgl.LoadFramebuffer();
        Rlgl.FramebufferAttach(
            fbo,
            rbo,
            FramebufferAttachType.Depth,
            FramebufferAttachTextureType.Renderbuffer,
            0
        );
        Rlgl.FramebufferAttach(
            fbo,
            cubeMap.Id,
            FramebufferAttachType.ColorChannel0,
            FramebufferAttachTextureType.CubemapPositiveX,
            0
        );

        // Check if framebuffer is complete with attachments (valid)
        if (Rlgl.FramebufferComplete(fbo))
        {
            Raylib.TraceLog(TraceLogLevel.Info, $"FBO: [ID {fbo}] Framebuffer object created successfully");
        }
        //------------------------------------------------------------------------------------------

        // STEP 2: Draw to framebuffer
        //------------------------------------------------------------------------------------------
        // NOTE: Shader is used to convert HDR equirectangular environment map to cubemap equivalent (6 faces)
        Rlgl.EnableShader(shader.Id);

        // Define projection matrix and send it to shader
        var matFboProjection = Raymath.MatrixPerspective(
            90.0f * Raylib.DEG2RAD,
            1.0f,
            Rlgl.CULL_DISTANCE_NEAR,
            Rlgl.CULL_DISTANCE_FAR
        );
        Rlgl.SetUniformMatrix(shader.Locs[(int)ShaderLocationIndex.MatrixProjection], matFboProjection);

        // Define view matrix for every side of the cubemap
        var fboViews = new[]
        {
            Raymath.MatrixLookAt(Vector3.Zero, new Vector3(-1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)),
            Raymath.MatrixLookAt(Vector3.Zero, new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)),
            Raymath.MatrixLookAt(Vector3.Zero, new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f)),
            Raymath.MatrixLookAt(Vector3.Zero, new Vector3(0.0f, -1.0f, 0.0f), new Vector3(0.0f, 0.0f, -1.0f)),
            Raymath.MatrixLookAt(Vector3.Zero, new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, -1.0f, 0.0f)),
            Raymath.MatrixLookAt(Vector3.Zero, new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, -1.0f, 0.0f)),
        };

        // Set viewport to current fbo dimensions
        Rlgl.Viewport(0, 0, size, size);

        // Activate and enable texture for drawing to cubemap faces
        Rlgl.ActiveTextureSlot(0);
        Rlgl.EnableTexture(panorama.Id);

        for (var i = 0; i < 6; i++)
        {
            // Set the view matrix for the current cube face
            Rlgl.SetUniformMatrix(shader.Locs[(int)ShaderLocationIndex.MatrixView], fboViews[i]);

            // Select the current cubemap face attachment for the fbo
            // WARNING: This function by default enables->attach->disables fbo!!!
            Rlgl.FramebufferAttach(
                fbo,
                cubeMap.Id,
                FramebufferAttachType.ColorChannel0,
                FramebufferAttachTextureType.CubemapPositiveX + i,
                0
            );
            Rlgl.EnableFramebuffer(fbo);

            // Load and draw a cube, it uses the current enabled texture
            Rlgl.ClearScreenBuffers();
            Rlgl.LoadDrawCube();
        }
        //------------------------------------------------------------------------------------------

        // STEP 3: Unload framebuffer and reset state
        //------------------------------------------------------------------------------------------
        Rlgl.DisableShader();
        Rlgl.DisableTexture();
        Rlgl.DisableFramebuffer();

        // Unload framebuffer (and automatically attached depth texture/renderbuffer)
        Rlgl.UnloadFramebuffer(fbo);

        // Reset viewport dimensions to default
        Rlgl.Viewport(0, 0, Rlgl.GetFramebufferWidth(), Rlgl.GetFramebufferHeight());
        Rlgl.EnableBackfaceCulling();
        //------------------------------------------------------------------------------------------

        cubeMap.Width = size;
        cubeMap.Height = size;
        cubeMap.Mipmaps = 1;
        cubeMap.Format = format;

        return cubeMap;
    }

}