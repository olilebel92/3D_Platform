import streamlit as st

import asset_io
import config


def _school_badge(school: str) -> str:
    color = config.SPELL_SCHOOL_COLORS.get(school, "#CCCCCC")
    return f'<span style="color:{color};font-weight:bold">{school}</span>'


def _hex_to_rgba(hex_color: str) -> dict:
    hex_color = hex_color.lstrip("#")
    r = int(hex_color[0:2], 16) / 255
    g = int(hex_color[2:4], 16) / 255
    b = int(hex_color[4:6], 16) / 255
    return {"r": r, "g": g, "b": b, "a": 0.45}


def _rgba_to_hex(c: dict) -> str:
    r = int(round(float(c.get("r", 1.0)) * 255))
    g = int(round(float(c.get("g", 1.0)) * 255))
    b = int(round(float(c.get("b", 1.0)) * 255))
    return f"#{r:02X}{g:02X}{b:02X}"


@st.cache_data
def _load_spells():
    return asset_io.scan_spells()


def _idx(options: list, value, fallback: int = 0) -> int:
    return options.index(value) if value in options else fallback


# ─── Form Renderer ───
# Shared by create + edit. `prefill` supplies defaults; `key_prefix` namespaces widgets.
def _render_spell_form(prefill: dict, key_prefix: str, lock_asset_name: bool) -> dict:
    # Core
    c1, c2 = st.columns(2)
    with c1:
        if lock_asset_name:
            st.text_input("Asset File Name", value=prefill["asset_file"], disabled=True,
                          key=f"{key_prefix}_asset")
            asset_name = prefill["asset_file"]
        else:
            asset_name = st.text_input("Asset File Name", placeholder="e.g. FrostBolt",
                                       key=f"{key_prefix}_asset")
        spell_name = st.text_input("Display Name", value=prefill.get("spellName", ""),
                                   placeholder="e.g. Frost Bolt", key=f"{key_prefix}_name")
    with c2:
        school_opts = list(config.SPELL_SCHOOL.values())
        type_opts   = list(config.SPELL_TYPE.values())
        school     = st.selectbox("School", school_opts,
            index=_idx(school_opts, prefill.get("school", "Arcane")), key=f"{key_prefix}_school")
        spell_type = st.selectbox("Spell Type", type_opts,
            index=_idx(type_opts, prefill.get("spellType", "Cast")), key=f"{key_prefix}_type")

    description = st.text_area("Description", value=prefill.get("description", ""),
        placeholder="Use {total}, {base}, {cooldown} tokens…", height=80,
        key=f"{key_prefix}_desc")

    # Combat
    with st.expander("Combat", expanded=True):
        cc1, cc2, cc3 = st.columns(3)
        base_damage          = cc1.number_input("Base Damage",
                                value=float(prefill.get("baseDamage", 0.0)),
                                min_value=0.0, step=5.0, key=f"{key_prefix}_bdmg")
        damage_per_rank      = cc2.number_input("Damage per Rank",
                                value=float(prefill.get("damagePerSkillRank", 0.0)),
                                min_value=0.0, step=1.0, key=f"{key_prefix}_dpr")
        cooldown             = cc3.number_input("Cooldown (s)",
                                value=float(prefill.get("cooldown", 2.0)),
                                min_value=0.0, step=0.5, key=f"{key_prefix}_cd")
        chain_count_per_rank = cc1.number_input("Chain Count/Rank",
                                value=int(prefill.get("chainCountPerRank", 0)),
                                min_value=0, step=1, key=f"{key_prefix}_ccpr")
        mana_cost            = cc2.number_input("Mana Cost",
                                value=float(prefill.get("manaCost", 0.0)),
                                min_value=0.0, step=5.0, key=f"{key_prefix}_mc")

    # Cast Timing
    with st.expander("Cast Timing"):
        ct1, ct2, ct3 = st.columns(3)
        cast_start_delay = ct1.number_input("Cast Start Delay",
                            value=float(prefill.get("castStartDelay", 0.0)),
                            min_value=0.0, step=0.05, key=f"{key_prefix}_csd")
        cast_time        = ct2.number_input("Cast Time",
                            value=float(prefill.get("castTime", 0.0)),
                            min_value=0.0, step=0.1, key=f"{key_prefix}_ct")
        throw_lead       = ct3.number_input("Throw Anim Lead",
                            value=float(prefill.get("throwAnimLeadTime", 0.0)),
                            min_value=0.0, step=0.05, key=f"{key_prefix}_tal")
        lock_move_cast   = ct1.checkbox("Lock Movement",
                            value=bool(prefill.get("lockMovementDuringCast", False)),
                            key=f"{key_prefix}_lmc")
        move_grace       = ct2.number_input("Movement Grace",
                            value=float(prefill.get("movementInterruptGrace", 0.0)),
                            min_value=0.0, step=0.1, key=f"{key_prefix}_mig")
        dmg_grace        = ct3.number_input("Damage Grace",
                            value=float(prefill.get("damageInterruptGrace", 0.0)),
                            min_value=0.0, step=0.1, key=f"{key_prefix}_dig")

    # Channel Settings
    with st.expander("Channel Settings"):
        ch1, ch2, ch3 = st.columns(3)
        channel_tick  = ch1.number_input("Tick Rate",
                            value=float(prefill.get("channelTickRate", 0.5)),
                            min_value=0.0, step=0.1, key=f"{key_prefix}_ctr")
        fire_on_start = ch2.checkbox("Fire on Start",
                            value=bool(prefill.get("fireOnChannelStart", False)),
                            key=f"{key_prefix}_fos")
        lock_move_ch  = ch3.checkbox("Lock Movement (Channel)",
                            value=bool(prefill.get("lockMovementDuringChannel", False)),
                            key=f"{key_prefix}_lmch")

    # Telegraph
    with st.expander("Telegraph"):
        shape_opts = list(config.TELEGRAPH_SHAPE.values())
        mode_opts  = list(config.COLOR_MODE.values())
        tg1, tg2 = st.columns(2)
        t_shape = tg1.selectbox("Shape", shape_opts,
                    index=_idx(shape_opts, prefill.get("telegraphShape", "None")),
                    key=f"{key_prefix}_tshape")
        t_mode  = tg2.selectbox("Color Mode", mode_opts,
                    index=_idx(mode_opts, prefill.get("telegraphColorMode", "Auto")),
                    key=f"{key_prefix}_tmode")
        tg3, tg4, tg5, tg6 = st.columns(4)
        t_radius = tg3.number_input("Radius",
                    value=float(prefill.get("telegraphRadius", 3.0)),
                    min_value=0.0, step=0.5, key=f"{key_prefix}_tr")
        t_angle  = tg4.number_input("Angle",
                    value=float(prefill.get("telegraphAngle", 90.0)),
                    min_value=0.0, step=5.0, key=f"{key_prefix}_ta")
        t_length = tg5.number_input("Length",
                    value=float(prefill.get("telegraphLength", 6.0)),
                    min_value=0.0, step=0.5, key=f"{key_prefix}_tl")
        t_width  = tg6.number_input("Width",
                    value=float(prefill.get("telegraphWidth", 0.5)),
                    min_value=0.0, step=0.1, key=f"{key_prefix}_tw")
        tg7, tg8 = st.columns(2)
        t_follows = tg7.checkbox("Follows Cursor",
                    value=bool(prefill.get("telegraphFollowsCursor", False)),
                    key=f"{key_prefix}_tfol")
        t_offset  = tg8.number_input("Origin Offset",
                    value=float(prefill.get("telegraphOriginOffset", 0.0)),
                    step=0.1, key=f"{key_prefix}_toff")
        default_hex = _rgba_to_hex(prefill.get("telegraphColor", {"r": 1.0, "g": 0.0, "b": 0.0})) \
                      if prefill.get("telegraphColor") else "#FF0000"
        t_color_hex = default_hex
        if t_mode == "Custom":
            t_color_hex = st.color_picker("Telegraph Color", default_hex, key=f"{key_prefix}_tcol")

    # Chain / Target-Locked
    with st.expander("Chain / Target-Locked"):
        tl1, tl2, tl3 = st.columns(3)
        cast_range   = tl1.number_input("Cast Range",
                        value=float(prefill.get("castRange", 10.0)),
                        min_value=0.0, step=1.0, key=f"{key_prefix}_crng")
        chain_count  = tl2.number_input("Chain Count",
                        value=int(prefill.get("chainCount", 0)),
                        min_value=0, step=1, key=f"{key_prefix}_cct")
        chain_radius = tl3.number_input("Chain Radius",
                        value=float(prefill.get("chainRadius", 6.0)),
                        min_value=0.0, step=0.5, key=f"{key_prefix}_crad")
        tl4, tl5, tl6 = st.columns(3)
        chain_falloff = tl4.slider("Chain Falloff", 0.1, 1.0,
                        value=float(prefill.get("chainDamageFalloff", 0.6)),
                        step=0.05, key=f"{key_prefix}_cfo")
        chain_travel  = tl5.number_input("Travel Time",
                        value=float(prefill.get("chainTravelTime", 0.2)),
                        min_value=0.0, step=0.05, key=f"{key_prefix}_ctt")
        chain_jump    = tl6.number_input("Jump Delay",
                        value=float(prefill.get("chainJumpDelay", 0.1)),
                        min_value=0.0, step=0.05, key=f"{key_prefix}_cjd")

    # Projectile
    with st.expander("Projectile"):
        origin_opts = list(config.SPAWN_ORIGIN.values())
        rot_opts    = list(config.SPAWN_ROTATION.values())
        pr1, pr2, pr3, pr4 = st.columns(4)
        proj_count   = pr1.number_input("Count",
                        value=int(prefill.get("projectileCount", 1)),
                        min_value=1, step=1, key=f"{key_prefix}_pc")
        spread_angle = pr2.number_input("Spread Angle",
                        value=float(prefill.get("spreadAngle", 0.0)),
                        min_value=0.0, step=5.0, key=f"{key_prefix}_psa")
        spawn_origin = pr3.selectbox("Spawn Origin", origin_opts,
                        index=_idx(origin_opts, prefill.get("spawnOrigin", "FirePoint")),
                        key=f"{key_prefix}_pso")
        spawn_rot    = pr4.selectbox("Spawn Rotation", rot_opts,
                        index=_idx(rot_opts, prefill.get("spawnRotation", "CameraAim")),
                        key=f"{key_prefix}_psr")

    telegraph_color = (
        _hex_to_rgba(t_color_hex) if t_mode == "Custom"
        else {"r": 1.0, "g": 1.0, "b": 1.0, "a": 0.45}
    )
    return {
        "asset_name":    asset_name,
        "spellName":     spell_name or asset_name,
        "description":   description,
        "school":        school,
        "spellType":     spell_type,
        "baseDamage":    base_damage,
        "damagePerSkillRank": damage_per_rank,
        "chainCountPerRank":  chain_count_per_rank,
        "cooldown":      cooldown,
        "manaCost":      mana_cost,
        "castStartDelay": cast_start_delay,
        "castTime":      cast_time,
        "throwAnimLeadTime": throw_lead,
        "lockMovementDuringCast": lock_move_cast,
        "movementInterruptGrace": move_grace,
        "damageInterruptGrace":   dmg_grace,
        "channelTickRate": channel_tick,
        "fireOnChannelStart": fire_on_start,
        "lockMovementDuringChannel": lock_move_ch,
        "spawnOrigin":   spawn_origin,
        "spawnRotation": spawn_rot,
        "projectileCount": proj_count,
        "spreadAngle":   spread_angle,
        "telegraphShape": t_shape,
        "telegraphRadius": t_radius,
        "telegraphAngle":  t_angle,
        "telegraphLength": t_length,
        "telegraphWidth":  t_width,
        "telegraphColorMode": t_mode,
        "telegraphColor": telegraph_color,
        "telegraphFollowsCursor": t_follows,
        "telegraphOriginOffset":  t_offset,
        "castRange":     cast_range,
        "chainCount":    chain_count,
        "chainRadius":   chain_radius,
        "chainDamageFalloff": chain_falloff,
        "chainTravelTime": chain_travel,
        "chainJumpDelay":  chain_jump,
    }


def render():
    col_left, col_right = st.columns([3, 1])
    with col_right:
        if st.button("Refresh Spells", key="refresh_spells"):
            st.cache_data.clear()
            st.rerun()

    spells = _load_spells()

    # ─── Library Table ───
    st.subheader(f"Spells ({len(spells)})")
    if not spells:
        st.info("No SpellData assets found.")
    else:
        table_html = (
            "<table style='width:100%;border-collapse:collapse'>"
            "<thead><tr style='border-bottom:1px solid #444;text-align:left'>"
            "<th style='padding:6px'>Asset File</th>"
            "<th style='padding:6px'>Spell Name</th>"
            "<th style='padding:6px'>School</th>"
            "<th style='padding:6px'>Type</th>"
            "<th style='padding:6px'>Base Dmg</th>"
            "<th style='padding:6px'>Cooldown</th>"
            "<th style='padding:6px'>Telegraph</th>"
            "</tr></thead><tbody>"
            + "".join(
                f"<tr style='border-bottom:1px solid #333'>"
                f"<td style='padding:5px'>{sp['asset_file']}</td>"
                f"<td style='padding:5px'>{sp['spellName']}</td>"
                f"<td style='padding:5px'>{_school_badge(sp['school'])}</td>"
                f"<td style='padding:5px'>{sp['spellType']}</td>"
                f"<td style='padding:5px'>{sp['baseDamage']}</td>"
                f"<td style='padding:5px'>{sp['cooldown']}s</td>"
                f"<td style='padding:5px'>{sp['telegraphShape']}</td>"
                f"</tr>"
                for sp in spells
            )
            + "</tbody></table>"
        )
        st.markdown(table_html, unsafe_allow_html=True)

    st.divider()

    # ─── Edit Existing ───
    st.subheader("Edit Existing Spell")
    if not spells:
        st.caption("No spells to edit yet.")
    else:
        asset_files = [sp["asset_file"] for sp in spells]
        selected = st.selectbox("Select a spell to edit", asset_files, key="spell_edit_select")
        prefill = next(sp for sp in spells if sp["asset_file"] == selected)

        with st.form(f"edit_spell_{selected}"):
            form_data = _render_spell_form(prefill, key_prefix=f"spell_edit_{selected}",
                                           lock_asset_name=True)
            save = st.form_submit_button("Save Changes", type="primary")

        if save:
            ok, msg = asset_io.update_spell_asset(selected, form_data)
            if ok:
                st.success(msg)
                st.cache_data.clear()
                st.rerun()
            else:
                st.error(msg)

    st.divider()

    # ─── Create New ───
    st.subheader("Create New Spell")
    with st.form("create_spell", clear_on_submit=True):
        form_data = _render_spell_form({"asset_file": ""}, key_prefix="spell_create",
                                       lock_asset_name=False)
        submitted = st.form_submit_button("Create Spell", type="primary")

    if submitted:
        ok, msg = asset_io.write_spell_asset(form_data)
        if ok:
            st.success(msg)
            st.cache_data.clear()
        else:
            st.error(msg)
