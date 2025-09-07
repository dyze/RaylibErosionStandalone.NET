#version 330
// Very simple skybox shader
// It only accepts a texture

// Input vertex attributes (from vertex shader)
in vec3 fragPosition;

// Input uniform values
uniform samplerCube environmentMap;
uniform int flipMode;       // 0=None, 1=Vertical only, 2=Horizontal only, 3=both 
uniform bool doGamma;

// Output fragment color
out vec4 finalColor;

void main()
{
    // Fetch color from texture map
    vec3 color = vec3(0.0);

    if (flipMode==1) color = texture(environmentMap, vec3(fragPosition.x, -fragPosition.y, fragPosition.z)).rgb;
    else if (flipMode==2) color = texture(environmentMap, vec3(-fragPosition.x, fragPosition.y, fragPosition.z)).rgb;
    else if (flipMode==3) color = texture(environmentMap, vec3(-fragPosition.x, -fragPosition.y, fragPosition.z)).rgb;
    else color = texture(environmentMap, fragPosition).rgb;

    if (doGamma)// Apply gamma correction
    {
        color = color/(color + vec3(1.0));
        color = pow(color, vec3(1.0/2.2));
    }

    // Calculate final fragment color
    finalColor = vec4(color, 1.0);
}
