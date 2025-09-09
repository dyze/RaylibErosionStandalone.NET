#version 330


// Input vertex attributes
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec3 vertexNormal;
in vec4 vertexColor;

// Input uniform values
uniform mat4 mvp;
uniform mat4 matModel;

// Output vertex attributes (to fragment shader)
out vec2 fragTexCoord;
out vec4 fragColor;
out vec3 fragPosition;
out vec4 clipSpace;

// NOTE: Add your custom variables here

const float tiling = 600.0; // size of repeated texture (higher -> smaller texture)


void main()
{
    // Send vertex attributes to fragment shader
    fragTexCoord = vertexTexCoord * tiling;
    fragColor = vertexColor;
    clipSpace = mvp*vec4(vertexPosition, 1.0);

    // Calculate final vertex position
    gl_Position = mvp*vec4(vertexPosition, 1.0);
}