shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/client_lighting.gdshaderinc"
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

#include "res://Shaders/liquid_reflection.gdshaderinc"

varying vec3 world_position;
void vertex() { world_position = (MODEL_MATRIX*vec4(VERTEX,1.0)).xyz; }

vec3 linear_to_gamma(vec3 c) {
    c = max(c, vec3(0.0));
    return mix(1.055*pow(c, vec3(1.0/2.4))-0.055, c*12.92, vec3(lessThanEqual(c, vec3(0.0031308))));
}

vec3 surface_normal(vec2 p, float time, float distance) {
    p *= wave_scale;
    if (!has_normals) return vec3(0.0, 1.0, 0.0);
    // procwaterabove: four RG samples, far amplitude 1.4 and distance blend .2+d*.001.
    // UV scales and motion substitute for stripped CPU texture matrices.
    vec2 near_sum = texture(ripple_tex, p*.09+vec2(.021,.013)*time).rg
        + texture(ripple_tex, vec2(p.y,-p.x)*.17+vec2(-.015,.019)*time).rg - 1.0;
    vec2 far_sum = (texture(ripple_tex, p*.023+vec2(.006,-.004)*time).rg
        + texture(ripple_tex, vec2(-p.y,p.x)*.047+vec2(-.005,-.007)*time).rg - 1.0)*1.4;
    vec2 slope = mix(far_sum, near_sum*2.0, min(.2+distance*.001,1.0))*normal_strength;
    slope *= inversesqrt(dot(slope,slope)+1.0);
    return normalize(vec3(slope.x,1.0,slope.y));
}

void fragment() {
    vec3 normal_world = surface_normal(world_position.xz,TIME,length(VERTEX));
    vec3 normal_view = normalize((VIEW_MATRIX*vec4(normal_world,0.0)).xyz);
    vec3 to_eye = normalize(-VERTEX);
    float fresnel = pow(1.0-clamp(dot(to_eye,normal_view),0.0,1.0),2.0);
    float thickness = max(view_depth(SCREEN_UV,INV_PROJECTION_MATRIX)+VERTEX.z,0.0);
    // procwaterabove_0: relative scene depth *100*(1.2-Fresnel), with
    // a perturbed-depth foreground test before using the refracted screen sample.
    float bend = clamp(thickness/max(-VERTEX.z,.001)*100.0*(1.2-fresnel),0.0,1.0);
    vec2 uv = clamp(SCREEN_UV+normal_view.xy*refraction_strength*bend,vec2(.001),vec2(.999));
    if (view_depth(uv,INV_PROJECTION_MATRIX)<-VERTEX.z) uv = SCREEN_UV;
    vec3 background = linear_to_gamma(texture(scene_color,uv).rgb);
    float depth = depth_table == 1 ? COLOR.a : min(round(COLOR.a*255.0)*.11111111*.21428572,1.0);
    // Client uses a linear interpolation of its supplied depth endpoints here.
    depth = depth*depth_scale+.1*abs(normal_world.z);
    vec4 shallow = gradient == 1 ? client_water_ocean_shallow : client_water_river_shallow;
    vec4 deep = gradient == 1 ? client_water_ocean_deep : client_water_river_deep;
    if (gradient == 2) shallow.rgb = deep.rgb;
    vec4 water = mix(shallow,deep,depth);
    vec3 light = client_sun_ambient+client_sun_diffuse*clamp(-client_sun_dir.y,0.0,1.0);
    vec3 body = water.rgb*light;
    vec3 reflection = linear_to_gamma(liquid_reflection(VERTEX,reflect(-to_eye,normal_view),client_gamma_to_linear(client_fog_color),SCREEN_UV,fwidth(SCREEN_UV),PROJECTION_MATRIX,INV_PROJECTION_MATRIX));
    // Retail's reflection response is drawn 60% toward the lit depth color.
    reflection = mix(reflection,body,.6);
    vec3 color = mix(background,body,clamp(water.a*(1.0-.1*fresnel),0.0,1.0));
    color = mix(color,reflection,.1+.5*fresnel);
    vec3 half_vector = normalize(-client_sun_dir+normalize((INV_VIEW_MATRIX*vec4(to_eye,0.0)).xyz));
    float highlight = pow(clamp(dot(half_vector,normal_world),0.0,1.0),max(specular_power,1.0))
        + max(normal_world.y-.8,0.0);
    color += highlight*client_sun_specular*fresnel;
    // Single-pass substitution for the client's final soft alpha: a cubic
    // scene-depth edge over 1.2*(.2+.5*Fresnel) yards. Foam/masks are unported.
    float edge = smoothstep(0.0,1.2*(.2+.5*fresnel),thickness);
    vec4 fog = client_fog_output(VERTEX,INV_VIEW_MATRIX);
    // Captured scene already contains fog. Apply surface fog to the final result;
    // this can attenuate captured contributions again, a documented limitation.
    vec3 surface = mix(client_gamma_to_linear(color),fog.rgb,fog.a);
    ALBEDO = mix(texture(scene_color,SCREEN_UV).rgb,surface,edge);
    ALPHA = 1.0;
}
