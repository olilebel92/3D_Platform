import pandas as pd
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


@st.cache_data
def _load_spells():
    return asset_io.scan_spells()


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

    # ─── Create Form ───
    st.subheader("Create New Spell")
    with st.form("create_spell", clear_on_submit=True):

        # Core
        c1, c2 = st.columns(2)
        with c1:
            asset_name = st.text_input("Asset File Name", placeholder="e.g. FrostBolt")
            spell_name = st.text_input("Display Name",    placeholder="e.g. Frost Bolt")
        with c2:
            school     = st.selectbox("School",     list(config.SPELL_SCHOOL.values()))
            spell_type = st.selectbox("Spell Type", list(config.SPELL_TYPE.values()))

        description = st.text_area("Description",
            placeholder="Use {total}, {base}, {cooldown} tokens…", height=80)

        # Combat
        with st.expander("Combat", expanded=True):
            cc1, cc2, cc3 = st.columns(3)
            base_damage         = cc1.number_input("Base Damage",        min_value=0.0, step=5.0)
            damage_per_rank     = cc2.number_input("Damage per Rank",    min_value=0.0, step=1.0)
            cooldown            = cc3.number_input("Cooldown (s)",       min_value=0.0, value=2.0, step=0.5)
            chain_count_per_rank = cc1.number_input("Chain Count/Rank",  min_value=0,   step=1)

        # Cast Timing
        with st.expander("Cast Timing"):
            ct1, ct2, ct3 = st.columns(3)
            cast_start_delay = ct1.number_input("Cast Start Delay",  min_value=0.0, step=0.05)
            cast_time        = ct2.number_input("Cast Time",         min_value=0.0, step=0.1)
            throw_lead       = ct3.number_input("Throw Anim Lead",   min_value=0.0, step=0.05)
            lock_move_cast   = ct1.checkbox("Lock Movement")
            move_grace       = ct2.number_input("Movement Grace",    min_value=0.0, step=0.1)
            dmg_grace        = ct3.number_input("Damage Grace",      min_value=0.0, step=0.1)

        # Channel Settings
        with st.expander("Channel Settings"):
            ch1, ch2, ch3 = st.columns(3)
            channel_tick     = ch1.number_input("Tick Rate",     min_value=0.0, value=0.5, step=0.1)
            fire_on_start    = ch2.checkbox("Fire on Start")
            lock_move_ch     = ch3.checkbox("Lock Movement (Channel)")

        # Telegraph
        with st.expander("Telegraph"):
            tg1, tg2 = st.columns(2)
            t_shape   = tg1.selectbox("Shape", list(config.TELEGRAPH_SHAPE.values()))
            t_mode    = tg2.selectbox("Color Mode", list(config.COLOR_MODE.values()))
            tg3, tg4, tg5, tg6 = st.columns(4)
            t_radius  = tg3.number_input("Radius",  min_value=0.0, value=3.0, step=0.5)
            t_angle   = tg4.number_input("Angle",   min_value=0.0, value=90.0, step=5.0)
            t_length  = tg5.number_input("Length",  min_value=0.0, value=6.0, step=0.5)
            t_width   = tg6.number_input("Width",   min_value=0.0, value=0.5, step=0.1)
            tg7, tg8 = st.columns(2)
            t_follows = tg7.checkbox("Follows Cursor")
            t_offset  = tg8.number_input("Origin Offset", step=0.1)
            t_color_hex = "#FF0000"
            if t_mode == "Custom":
                t_color_hex = st.color_picker("Telegraph Color", "#FF0000")

        # Chain / Target-Locked
        with st.expander("Chain / Target-Locked"):
            tl1, tl2, tl3 = st.columns(3)
            cast_range   = tl1.number_input("Cast Range",   min_value=0.0, value=10.0, step=1.0)
            chain_count  = tl2.number_input("Chain Count",  min_value=0,   step=1)
            chain_radius = tl3.number_input("Chain Radius", min_value=0.0, value=6.0, step=0.5)
            tl4, tl5, tl6 = st.columns(3)
            chain_falloff = tl4.slider("Chain Falloff", 0.1, 1.0, 0.6, step=0.05)
            chain_travel  = tl5.number_input("Travel Time", min_value=0.0, value=0.2, step=0.05)
            chain_jump    = tl6.number_input("Jump Delay",  min_value=0.0, value=0.1, step=0.05)

        # Projectile
        with st.expander("Projectile"):
            pr1, pr2, pr3, pr4 = st.columns(4)
            proj_count    = pr1.number_input("Count",  min_value=1, value=1, step=1)
            spread_angle  = pr2.number_input("Spread Angle", min_value=0.0, step=5.0)
            spawn_origin  = pr3.selectbox("Spawn Origin",   list(config.SPAWN_ORIGIN.values()))
            spawn_rot     = pr4.selectbox("Spawn Rotation", list(config.SPAWN_ROTATION.values()))

        submitted = st.form_submit_button("Create Spell", type="primary")

    if submitted:
        telegraph_color = (
            _hex_to_rgba(t_color_hex) if t_mode == "Custom"
            else {"r": 1.0, "g": 1.0, "b": 1.0, "a": 0.45}
        )
        ok, msg = asset_io.write_spell_asset({
            "asset_name":    asset_name,
            "spellName":     spell_name or asset_name,
            "description":   description,
            "school":        school,
            "spellType":     spell_type,
            "baseDamage":    base_damage,
            "damagePerSkillRank": damage_per_rank,
            "chainCountPerRank":  chain_count_per_rank,
            "cooldown":      cooldown,
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
        })
        if ok:
            st.success(msg)
            st.cache_data.clear()
        else:
            st.error(msg)
