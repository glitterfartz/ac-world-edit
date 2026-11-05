// The scrolling ripple normal shared by the retail water surface shaders: "mediumwater" (the stock water
// types, drawn by liquid.gdshader) and "procwater" (the procedural ones, drawn by procwater.gdshader).
// Both sample the LiquidType bump map four times, as two fine pairs and two coarse pairs. Above the
// surface the pairs blend by camera distance; procwaterbelow instead folds both at full strength, which
// is the client's short-range look from inside the water. The CPU texture matrices behind the UVs are not
// shipped, so the scales and scroll rates here are estimates, as noted in the shaders that call this.
#ifndef LIQUID_SURFACE_INC
#define LIQUID_SURFACE_INC

// world_xz: horizontal world position. distance: yards to the eye. ripple_scale: the type's wave scale.
// underwater: compose the way procwaterbelow does. Returns the ripple slope, normalised against its
// own height so a large scale cannot tip the surface past vertical.
vec2 liquid_ripple(sampler2D ripple, vec2 world_xz, float time, float distance, float ripple_scale, bool underwater) {
    vec2 near_sum = texture(ripple, world_xz * 0.090 + vec2(0.021, 0.013) * time).rg
        + texture(ripple, vec2(world_xz.y, -world_xz.x) * 0.170 + vec2(-0.015, 0.019) * time).rg - 1.0;
    vec2 far_sum = texture(ripple, world_xz * 0.023 + vec2(0.006, -0.004) * time).rg
        + texture(ripple, vec2(-world_xz.y, world_xz.x) * 0.047 + vec2(-0.005, -0.007) * time).rg - 1.0;
    vec2 slope = underwater
        ? near_sum * 2.0 + far_sum * 2.0 * ripple_scale
        : ((near_sum * 2.0 - far_sum * 1.4) * min(0.2 + distance * 0.001, 1.0) + far_sum * 1.4) * ripple_scale;
    return slope * inversesqrt(dot(slope, slope) + 1.0);
}

#endif
