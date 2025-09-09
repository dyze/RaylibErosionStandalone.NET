// rlights.cs - Some useful functions to deal with lights data
// This is the C# version of rlights.h: https://github.com/raysan5/raylib/blob/master/examples/shaders/rlights.h
//
// It is available for free. Do whatever you want with it.
//
// Such implementation only works with provided fs and vs shader files
//
// Author: dyze@dlabs.eu

using System.Numerics;
using Raylib_cs;

// Must match values in fs shader file
public enum LightType
{
    Directional = 0,
    Point = 1
}


public class Light
{
    public LightType Type;
    public Vector3 Position;
    public Vector3 Target;
    public Color Color;
    public bool Enabled;

    // Shader locations
    public List<int> EnabledLoc = [];
    public List<int> TypeLoc = [];
    public List<int> PosLoc = [];
    public List<int> TargetLoc = [];
    public List<int> ColorLoc = [];

    // shaders that use light
    public List<Shader> Shaders;
};


public static class Rlights
{
    public const int MaxLights = 4;         // Max dynamic lights supported by shader
    private static int _lightsCount = 0;

    /// <summary>
    /// Create a light and get shader locations
    /// </summary>
    /// <param name="type"></param>
    /// <param name="position"></param>
    /// <param name="target"></param>
    /// <param name="color"></param>
    /// <param name="shaders"></param>
    /// <returns></returns>
    /// <exception cref="IndexOutOfRangeException"></exception>

    public static Light CreateLight(LightType type, Vector3 position, Vector3 target, Color color, List<Shader> shaders)
    {
        Light light = new();

        if (_lightsCount >= MaxLights)
            throw new IndexOutOfRangeException($"Count of lights {_lightsCount} exceeds {MaxLights}");

        light.Enabled = true;
        light.Type = type;
        light.Position = position;
        light.Target = target;
        light.Color = color;

        // names below must match those defined in shader fs files

        var enabledName = $"lights[{_lightsCount}].enabled";
        var typeName = $"lights[{_lightsCount}].type";
        var posName = $"lights[{_lightsCount}].position";
        var targetName = $"lights[{_lightsCount}].target";
        var colorName = $"lights[{_lightsCount}].color";

        foreach (var shader in shaders)
        {
            light.EnabledLoc.Add(Raylib.GetShaderLocation(shader, enabledName));
            light.TypeLoc.Add(Raylib.GetShaderLocation(shader, typeName));
            light.PosLoc.Add(Raylib.GetShaderLocation(shader, posName));
            light.TargetLoc.Add(Raylib.GetShaderLocation(shader, targetName));
            light.ColorLoc.Add(Raylib.GetShaderLocation(shader, colorName));
        }

        light.Shaders = shaders;

        UpdateLightValues(light);

        _lightsCount++;


        return light;
    }

    /// <summary>
    /// Send light properties to shader
    ///  NOTE: Light shader locations should be available 
    /// </summary>
    /// <param name="light"></param>
    public static void UpdateLightValues(Light light)
    {
        for (var i = 0; i < light.Shaders.Count(); i++)
        {
            // Send to shader light enabled state and type
            var enabled = light.Enabled ? 1: 0;
            Raylib.SetShaderValue(light.Shaders[i], light.EnabledLoc[i], enabled, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(light.Shaders[i], light.TypeLoc[i], light.Type, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(light.Shaders[i], light.PosLoc[i], light.Position, ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(light.Shaders[i], light.TargetLoc[i], light.Target, ShaderUniformDataType.Vec3);

            // Send to shader light color values
            var color = new Vector4(light.Color.R / (float)255, light.Color.G / (float)255,
                light.Color.B / (float)255, light.Color.A / (float)255);
            Raylib.SetShaderValue(light.Shaders[i], light.ColorLoc[i], color, ShaderUniformDataType.Vec4);
        }
    }

    public static void SetAmbientColor(Shader shader, System.Drawing.Color ambientColor)
    {
        var ambientLoc = Raylib.GetShaderLocation(shader, "ambient");

        Vector4 color = new Vector4((float)ambientColor.R / (float)255, (float)ambientColor.G / (float)255,
            (float)ambientColor.B / (float)255, (float)ambientColor.A / (float)255);

        Raylib.SetShaderValue(shader, ambientLoc, color, ShaderUniformDataType.Vec4);
    }
}




