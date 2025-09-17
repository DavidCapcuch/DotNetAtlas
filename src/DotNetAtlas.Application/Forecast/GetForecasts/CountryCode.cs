using System.Text.Json.Serialization;

namespace DotNetAtlas.Application.Forecast.GetForecasts;

// ISO 3166-1 alpha-2 official assignments (uppercase two-letter codes)
// Reference: https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2
// Ensure JSON uses strings for this enum (affects request/response payloads)
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CountryCode
{
    Ad, Ae, Af, Ag, Ai, Al, Am, Ao, Aq, Ar, As, At, Au, Aw, Ax, Az,
    Ba, Bb, Bd, Be, Bf, Bg, Bh, Bi, Bj, Bl, Bm, Bn, Bo, Bq, Br, Bs, Bt, Bv, Bw, By, Bz,
    Ca, Cc, Cd, Cf, Cg, Ch, Ci, Ck, Cl, Cm, Cn, Co, Cr, Cu, Cv, Cw, Cx, Cy, Cz,
    De, Dj, Dk, Dm, Do, Dz,
    Ec, Ee, Eg, Eh, Er, Es, Et,
    Fi, Fj, Fk, Fm, Fo, Fr,
    Ga, Gb, Gd, Ge, Gf, Gg, Gh, Gi, Gl, Gm, Gn, Gp, Gq, Gr, Gs, Gt, Gu, Gw, Gy,
    Hk, Hm, Hn, Hr, Ht, Hu,
    Id, Ie, Il, Im, In, Io, Iq, Ir, Is, It,
    Je, Jm, Jo, Jp,
    Ke, Kg, Kh, Ki, Km, Kn, Kp, Kr, Kw, Ky, Kz,
    La, Lb, Lc, Li, Lk, Lr, Ls, Lt, Lu, Lv, Ly,
    Ma, Mc, Md, Me, Mf, Mg, Mh, Mk, Ml, Mm, Mn, Mo, Mp, Mq, Mr, Ms, Mt, Mu, Mv, Mw, Mx, My, Mz,
    Na, Nc, Ne, Nf, Ng, Ni, Nl, No, Np, Nr, Nu, Nz,
    Om,
    Pa, Pe, Pf, Pg, Ph, Pk, Pl, Pm, Pn, Pr, Ps, Pt, Pw, Py,
    Qa,
    Re, Ro, Rs, Ru, Rw,
    Sa, Sb, Sc, Sd, Se, Sg, Sh, Si, Sj, Sk, Sl, Sm, Sn, So, Sr, Ss, St, Sv, Sx, Sy, Sz,
    Tc, Td, Tf, Tg, Th, Tj, Tk, Tl, Tm, Tn, To, Tr, Tt, Tv, Tw, Tz,
    Ua, Ug, Um, Us, Uy, Uz,
    Va, Vc, Ve, Vg, Vi, Vn, Vu,
    Wf, Ws,
    Ye, Yt,
    Za, Zm, Zw
}
