import streamlit as st

import asset_io
import config


def _category_badge(category: str) -> str:
    color = config.ENEMY_CATEGORY_COLORS.get(category, "#CCCCCC")
    return f'<span style="color:{color};font-weight:bold">{category}</span>'


@st.cache_data
def _load_enemies():
    return asset_io.scan_enemies()


def render():
    col_left, col_right = st.columns([3, 1])
    with col_right:
        if st.button("Refresh Enemies", key="refresh_enemies"):
            st.cache_data.clear()
            st.rerun()

    enemies = _load_enemies()

    # ─── Library Table ───
    st.subheader(f"Enemies ({len(enemies)})")
    if not enemies:
        st.info("No EnemyData assets found. Create one below or open Unity.")
    else:
        table_html = (
            "<table style='width:100%;border-collapse:collapse'>"
            "<thead><tr style='border-bottom:1px solid #444;text-align:left'>"
            "<th style='padding:6px'>Asset File</th>"
            "<th style='padding:6px'>Name</th>"
            "<th style='padding:6px'>Creature</th>"
            "<th style='padding:6px'>Category</th>"
            "<th style='padding:6px'>HP</th>"
            "<th style='padding:6px'>DMG</th>"
            "<th style='padding:6px'>XP</th>"
            "</tr></thead><tbody>"
            + "".join(
                f"<tr style='border-bottom:1px solid #333'>"
                f"<td style='padding:5px'>{e['asset_file']}</td>"
                f"<td style='padding:5px'>{e['enemyName']}</td>"
                f"<td style='padding:5px'>{e['creatureType']}</td>"
                f"<td style='padding:5px'>{_category_badge(e['category'])}</td>"
                f"<td style='padding:5px'>{int(e['maxHealth'])}</td>"
                f"<td style='padding:5px'>{e['attackDamage']}</td>"
                f"<td style='padding:5px'>{e['xpReward']}</td>"
                f"</tr>"
                for e in enemies
            )
            + "</tbody></table>"
        )
        st.markdown(table_html, unsafe_allow_html=True)

    st.divider()

    # ─── Create Form ───
    st.subheader("Create New Enemy")
    with st.form("create_enemy", clear_on_submit=True):
        col1, col2 = st.columns(2)
        with col1:
            asset_name = st.text_input("Asset File Name", placeholder="e.g. SkeletonWarrior")
            enemy_name = st.text_input("Display Name",    placeholder="e.g. Skeleton Warrior")
        with col2:
            creature_type = st.selectbox("Creature Type", list(config.CREATURE_TYPE.values()))
            category      = st.selectbox("Category",      list(config.ENEMY_CATEGORY.values()))

        description = st.text_area("Description", placeholder="Flavour text or lore…", height=70)

        st.markdown("**Combat Stats**")
        sc1, sc2, sc3 = st.columns(3)
        with sc1:
            max_health      = st.number_input("Max Health",       value=10.0,  min_value=1.0,  step=1.0)
            attack_damage   = st.number_input("Attack Damage",    value=1,     min_value=1,    step=1)
        with sc2:
            move_speed      = st.number_input("Move Speed",       value=3.0,   min_value=0.1,  step=0.1)
            attack_cooldown = st.number_input("Attack Cooldown",  value=1.5,   min_value=0.1,  step=0.1)
        with sc3:
            attack_range    = st.number_input("Attack Range",     value=2.0,   min_value=0.5,  step=0.5)
            detection_range = st.number_input("Detection Range",  value=10.0,  min_value=1.0,  step=1.0)

        st.markdown("**Rotation**")
        rr1, rr2 = st.columns(2)
        with rr1:
            angular_speed  = st.number_input("Angular Speed (chase, deg/s)",  value=200.0, min_value=1.0, step=10.0)
        with rr2:
            rotation_speed = st.number_input("Rotation Speed (attack, deg/s)", value=200.0, min_value=1.0, step=10.0)

        st.markdown("**Stun & Misc**")
        sm1, sm2, sm3 = st.columns(3)
        with sm1:
            attack_stun_chance   = st.slider("Stun Chance",    min_value=0.0, max_value=1.0, value=0.2, step=0.05)
        with sm2:
            attack_stun_duration = st.number_input("Stun Duration",   value=1.0,  min_value=0.0,  step=0.1)
        with sm3:
            retarget_interval    = st.number_input("Retarget Interval", value=1.0, min_value=0.1, step=0.1)

        st.markdown("**Rewards**")
        rw1, rw2, rw3 = st.columns(3)
        with rw1:
            xp_reward        = st.number_input("XP Reward",       value=50,   min_value=0,    step=5)
        with rw2:
            give_hp_on_death = st.checkbox("Give HP on Death")
        with rw3:
            hp_reward_on_death = st.number_input("HP Reward",     value=1,    min_value=0,    step=1)

        submitted = st.form_submit_button("Create Enemy", type="primary")

    if submitted:
        ok, msg = asset_io.write_enemy_asset({
            "asset_name":          asset_name,
            "enemy_name":          enemy_name or asset_name,
            "description":         description,
            "creature_type":       creature_type,
            "category":            category,
            "max_health":          max_health,
            "move_speed":          move_speed,
            "attack_damage":       attack_damage,
            "attack_cooldown":     attack_cooldown,
            "attack_range":        attack_range,
            "detection_range":     detection_range,
            "attack_stun_chance":  attack_stun_chance,
            "attack_stun_duration": attack_stun_duration,
            "retarget_interval":   retarget_interval,
            "angular_speed":       angular_speed,
            "rotation_speed":      rotation_speed,
            "xp_reward":           xp_reward,
            "give_hp_on_death":    give_hp_on_death,
            "hp_reward_on_death":  hp_reward_on_death,
        })
        if ok:
            st.success(msg)
            st.cache_data.clear()
        else:
            st.error(msg)
