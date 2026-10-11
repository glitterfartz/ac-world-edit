shader_type spatial;
render_mode cull_disabled;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D idle_palette : filter_nearest, repeat_disable;
uniform float idle_duration = 0.0;
uniform int idle_frames = 1;
uniform bool skinned = false;
uniform int blend_mode = 0;
uniform bool vertex_color = false;

mat4 bone_matrix(int bone, int frame) {
    int x = bone * 4;
    return mat4(texelFetch(idle_palette, ivec2(x,frame),0), texelFetch(idle_palette,ivec2(x+1,frame),0),
        texelFetch(idle_palette,ivec2(x+2,frame),0), texelFetch(idle_palette,ivec2(x+3,frame),0));
}
void vertex() {
    if (skinned) {
        float frame = idle_duration > 0.0 ? mod(TIME,idle_duration) / idle_duration * float(idle_frames-1) : 0.0;
        int a = int(floor(frame)); int b = min(a+1,idle_frames-1); float f = fract(frame);
        vec4 p = vec4(0.0); vec3 n = vec3(0.0); float total = 0.0;
        for (int i=0; i<4; i++) {
            float w = CUSTOM1[i]; int bone = int(CUSTOM0[i]);
            if (w > 0.0) {
                mat4 ma = bone_matrix(bone,a); mat4 mb = bone_matrix(bone,b);
                p += mix(ma*vec4(VERTEX,1.0),mb*vec4(VERTEX,1.0),f)*w;
                n += mix(mat3(ma)*NORMAL,mat3(mb)*NORMAL,f)*w; total += w;
            }
        }
        if (total > 0.0) { VERTEX = p.xyz/total; NORMAL = normalize(n); }
    }
}
void fragment() {
    vec4 tex = texture(albedo_tex,UV);
    if (blend_mode == 1 && tex.a < 0.5) discard;
    ALBEDO = tex.rgb * (vertex_color ? COLOR.rgb : vec3(1.0));
    ROUGHNESS = 1.0; SPECULAR = 0.0;
}
