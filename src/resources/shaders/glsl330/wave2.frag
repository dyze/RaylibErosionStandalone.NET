#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec4 fragColor;
in vec4 clipSpace;
in vec3 vertexPosition;
in vec3 fragPosition;

// Input uniform values
uniform sampler2D texture0; // reflection
uniform sampler2D texture1; // refraction
uniform sampler2D texture2; // DUDVMap
uniform vec4 colDiffuse;

// Output fragment color
out vec4 finalColor;

uniform float seconds;

uniform vec2 size;

uniform float freqX;
uniform float freqY;
uniform float ampX;
uniform float ampY;
uniform float speedX;
uniform float speedY;

#define     MAX_LIGHTS              1
#define     LIGHT_DIRECTIONAL       0
#define     LIGHT_POINT             1
struct Light {
    int enabled;
    int type;
    vec3 position;
    vec3 target;
    vec4 color;
};

uniform float moveFactor;

// Input lighting values
uniform Light lights[MAX_LIGHTS];
uniform vec4 ambient;
uniform vec3 viewPos;

const float waveStrength = 0.03; // intensity of wave distortion
const vec4 waterColor = vec4(0.11, 0.639, 0.925, 1.0);//vec4(0.11, 0.639, 0.925, 1.0); // base color of water


void main() {
	float specularWater = 0.0;
	float specularPlane = 0.0;
    vec3 viewD = normalize(viewPos - fragPosition); // view versor

	vec2 normalizedDeviceSpace = (clipSpace.xy/clipSpace.w)/2.0 + 0.5; // fragment coordinates in screen space

    float pixelWidth = 1.0/size.x;
    float pixelHeight = 1.0/size.y;
    float aspect = pixelHeight/pixelWidth;
    float boxLeft = 0.0;
    float boxTop = 0.0;

    vec2 p = fragTexCoord;
    p.x += cos((fragTexCoord.y - boxTop)*freqX/(pixelWidth*750.0) + (seconds*speedX))*ampX*pixelWidth;
    p.y += sin((fragTexCoord.x - boxLeft)*freqY*aspect/(pixelHeight*750.0) + (seconds*speedY))*ampY*pixelHeight;

    vec2 distortedTexCoords = p;

    vec2 totalDistortion = (texture2D(texture2, distortedTexCoords).xy * 2.0 - 1.0) * waveStrength;

	vec3 normal = normalize(vec3(totalDistortion.x*50.0 ,1.0, totalDistortion.y*50.0));//vec3(0,1,0);//normalize(vec3(normalMapColor.r*2.0 -1.0, normalMapColor.b, normalMapColor.g*2.0 -1.0));

	float fresnel = dot(viewD, vec3(0,1,0));  //fresnel value (0 = looking horizon, 1 = looking downward)
	fresnel = pow(fresnel, 0.1);// 0.325);  //0.285); // reduce reflection in favor of refraction due to pow < 1

    //float fresnel = 0.5;

	for (int i = 0; i < 1; i++)//MAX_LIGHTS; i++)
    {
        vec3 lightD = -normalize(lights[i].target - lights[i].position); //normalize(lights[i].position - fragPosition);
		float NdotLPlane = max(1.0 + min(dot(vec3(0,1,0), lightD), 0.0) * 2.0, 0.0);
		specularWater = NdotLPlane*pow(max(0.0, dot(viewD, reflect(-(lightD), normal))), 24.0); // specular according to waves
	}

	vec2 reflectTexCoords = vec2(normalizedDeviceSpace.x, 1.0-normalizedDeviceSpace.y);
	vec2 refractTexCoords = vec2(normalizedDeviceSpace.x, normalizedDeviceSpace.y);

	reflectTexCoords = clamp(reflectTexCoords+totalDistortion, 0.01, 0.99);
	refractTexCoords = clamp(refractTexCoords+totalDistortion, 0.01, 0.99);

    vec4 reflectColor = texture2D(texture0, reflectTexCoords);
	vec4 refractColor = texture2D(texture1, refractTexCoords);

	float waterColorStrength = 0.1;
	finalColor = mix(reflectColor, refractColor, fresnel);
	finalColor = mix(finalColor, waterColor, waterColorStrength);
	finalColor = finalColor + specularWater + specularPlane;
}
