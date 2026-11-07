shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/shared/client_lighting.gdshaderinc"
global uniform vec4 client_water_ocean_shallow;
global uniform vec4 client_water_ocean_deep;
global uniform vec4 client_water_river_shallow;
global uniform vec4 client_water_river_deep;
uniform sampler2D ripple_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D scene_color : hint_screen_texture, filter_linear, repeat_disable;
uniform sampler2D scene_depth : hint_depth_texture, filter_nearest, repeat_disable;
uniform bool has_normals = false;
uniform int gradient = 1;
uniform int depth_table = 1;
uniform float depth_scale = 1.0;
// DB2 bindings are inferred. CPU UV matrices still use estimated rates/scales.
uniform float wave_scale = 1.0;
uniform float normal_strength = .5;
uniform float refraction_strength = .1;
uniform float specular_power = 300.0;

#include "res://Shaders/liquid/liquid_reflection.gdshaderinc"
#include "res://Shaders/liquid/liquid_surface.gdshaderinc"

// The procedural water surface: "procwaterabove" above the waterline and "procwaterbelow" underneath.
// Both read the type's native bump map, its depth colors and the stored depth byte; the client picks
// the pair by the eye's side of the surface, which this viewer does per fragment because one material
// covers every surface patch of a type. The underside has no reflection input in the shipped shader,
// no foam, a plain ripple refraction offset and the gradient alpha as the weight of the water body.
// It writes an opaque, already-composited image, so there is no shore fade.

varying vec3 world_position;
void vertex() { world_position = (MODEL_MATRIX*vec4(VERTEX,1.0)).xyz; }

vec3 linear_to_gamma(vec3 c) {
    c = max(c, vec3(0.0));
    return mix(1.055*pow(c, vec3(1.0/2.4))-0.055, c*12.92, vec3(lessThanEqual(c, vec3(0.0031308))));
}

vec3 surface_normal(vec2 p, float time, float distance, bool below) {
    // Under the surface the client builds the normal facing the viewer, not world up.
    float vertical = below ? -1.0 : 1.0;
    if (!has_normals) return vec3(0.0, vertical, 0.0);
    // procwaterabove blends a near and a far pair; procwaterbelow folds both at full strength.
    // UV scales and motion substitute for stripped CPU texture matrices.
    vec2 slope = liquid_ripple(ripple_tex, p, time, distance, wave_scale, below) * normal_strength;
    return normalize(vec3(slope.x, vertical, slope.y));
}

void fragment() {
    bool below = INV_VIEW_MATRIX[3].y < world_position.y;
    vec3 normal_world = surface_normal(world_position.xz,TIME,length(VERTEX),below);
    vec3 normal_view = normalize((VIEW_MATRIX*vec4(normal_world,0.0)).xyz);
    vec3 to_eye = normalize(-VERTEX);
    float fresnel = pow(1.0-clamp(dot(to_eye,normal_view),0.0,1.0),2.0);
    float thickness = max(view_depth(SCREEN_UV,INV_PROJECTION_MATRIX)+VERTEX.z,0.0);
    // procwaterabove_0: relative scene depth *100*(1.2-Fresnel) bends the refraction, with a
    // perturbed-depth foreground test before using the refracted screen sample. procwaterbelow_0
    // skips the depth term and offsets by the ripple alone, but rejects the same foreground.
    vec2 uv;
    if (below) {
        uv = clamp(SCREEN_UV+normal_view.xy*refraction_strength,vec2(.001),vec2(.999));
        if (view_depth(uv,INV_PROJECTION_MATRIX)<-VERTEX.z) uv = SCREEN_UV;
    } else {
        float bend = clamp(thickness/max(-VERTEX.z,.001)*100.0*(1.2-fresnel),0.0,1.0);
        uv = clamp(SCREEN_UV+normal_view.xy*refraction_strength*bend,vec2(.001),vec2(.999));
        if (view_depth(uv,INV_PROJECTION_MATRIX)<-VERTEX.z) uv = SCREEN_UV;
    }
    vec3 background = linear_to_gamma(texture(scene_color,uv).rgb);
    float depth = depth_table == 1 ? COLOR.a : min(round(COLOR.a*255.0)*.11111111*.21428572,1.0);
    // The client tilts the depth gradient with the ripple above the surface only.
    depth = depth*depth_scale+(below ? 0.0 : .1*abs(normal_world.z));
    vec4 shallow = gradient == 1 ? client_water_ocean_shallow : client_water_river_shallow;
    vec4 deep = gradient == 1 ? client_water_ocean_deep : client_water_river_deep;
    if (gradient == 2) shallow.rgb = deep.rgb;
    vec4 water = mix(shallow,deep,depth);
    vec3 light = client_sun_ambient+client_sun_diffuse*clamp(-client_sun_dir.y,0.0,1.0);
    vec3 body = water.rgb*light;
    // Retail's body weight is drawn a tenth back at grazing angles on both sides.
    vec3 color = mix(background,body,clamp(water.a*(1.0-.1*fresnel),0.0,1.0));
    vec3 half_vector = normalize(-client_sun_dir+normalize((INV_VIEW_MATRIX*vec4(to_eye,0.0)).xyz));
    float highlight = pow(clamp(dot(half_vector,normal_world),0.0,1.0),max(specular_power,1.0))
        + max(normal_world.y-.8,0.0);
    if (below) {
        // procwaterbelow_0: no reflection (the shipped shader has no reflection input) and no foam;
        // the sun's underwater response is 40 % of the above-water one behind a fourth-power Fresnel
        // and scaled by the gradient alpha.
        color += .4*fresnel*fresnel*water.a*highlight*client_sun_specular;
    } else {
        vec3 view_dir = reflect(-to_eye,normal_view);
        vec3 world_dir = normalize((INV_VIEW_MATRIX * vec4(view_dir, 0.0)).xyz);
        vec3 reflection = linear_to_gamma(liquid_reflection(VERTEX,view_dir,world_dir,client_gamma_to_linear(client_fog_color),SCREEN_UV,fwidth(SCREEN_UV),PROJECTION_MATRIX,INV_PROJECTION_MATRIX));
        // Retail's reflection response is drawn 60% toward the lit depth color.
        reflection = mix(reflection,body,.6);
        color = mix(color,reflection,.1+.5*fresnel);
        color += highlight*client_sun_specular*fresnel;
    }
    // Single-pass substitution for the client's final soft alpha: a cubic scene-depth edge over
    // 1.2*(.2+.5*Fresnel) yards. Foam/masks are unported. The below path has no edge in the client:
    // its color already holds the refracted scene and it writes an opaque pixel.
    vec4 fog = client_fog_output(VERTEX, INV_VIEW_MATRIX);
    // Captured scene already contains fog. Apply surface fog to the final result; this can attenuate
    // captured contributions again, a documented limitation.
    vec3 surface = mix(client_gamma_to_linear(color),fog.rgb,fog.a);
    float edge = below ? 1.0 : smoothstep(0.0,1.2*(.2+.5*fresnel),thickness);
    ALBEDO = mix(texture(scene_color,SCREEN_UV).rgb,surface,edge);
    ALPHA = 1.0;
}
