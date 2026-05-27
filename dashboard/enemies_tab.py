import streamlit as st

import asset_io
import config


def _category_badge(category: str) -> str:
    color = config.ENEMY_CATEGORY_COLORS.get(category, "#CCCCCC")
    return (
        f'<span style="border:1px solid {color};color:{color};'
        f'padding:2px 10px;border-radius:10px;font-weight:600;'
        f'font-size:0.80em;letter-spacing:0.5px;display:inline-block">'
        f'{category.upper()}</span>'
    )


@st.cache_data
def _load_enemies():
    return asset_io.scan_enemies()


def _clear_edit_widget_state(asset_file: str) -> None:
    """Drop any persisted widget state for the edit form so a fresh prefill applies."""
    prefix = f"enemy_edit_{asset_file}_"
    for k in [k for k in list(st.session_state.keys()) if k.startswith(prefix)]:
        del st.session_state[k]


# ─── Form Renderer ───
# Shared by both "Create New" and "Edit Existing". `prefill` carries default values;
# `key_prefix` namespaces widget keys so create and edit forms don't collide. The
# asset filename is editable only on create — locked on edit (file rename is out of scope).
def _render_enemy_form(prefill: dict, key_prefix: str, lock_asset_name: bool) -> dict:
    col1, col2 = st.columns(2)
    with col1:
        if lock_asset_name:
            st.text_input("Asset File Name", value=prefill["asset_file"], disabled=True,
                          key=f"{key_prefix}_asset")
            asset_name = prefill["asset_file"]
        else:
            asset_name = st.text_input("Asset File Name", placeholder="e.g. SkeletonWarrior",
                                       key=f"{key_prefix}_asset")
        enemy_name = st.text_input("Display Name", value=prefill.get("enemyName", ""),
                                   placeholder="e.g. Skeleton Warrior", key=f"{key_prefix}_name")
        cat_options = list(config.ENEMY_CATEGORY.values())
        category = st.selectbox("Category", cat_options,
            index=cat_options.index(prefill.get("category", cat_options[0]))
                  if prefill.get("category") in cat_options else 0,
            key=f"{key_prefix}_category")
        level = st.number_input("Level", min_value=1, max_value=100,
                                value=int(prefill.get("level", 1)), step=1,
                                key=f"{key_prefix}_level")
    with col2:
        creature_options = list(config.CREATURE_TYPE.values())
        prefill_types = [t for t in (prefill.get("creatureTypes") or [creature_options[0]])
                         if t in creature_options] or [creature_options[0]]
        creature_types = st.multiselect("Creature Types", creature_options,
                                        default=prefill_types,
                                        key=f"{key_prefix}_creature_types",
                                        help="Pick one or more — an enemy can belong to multiple races.")

    description = st.text_area("Description", value=prefill.get("description", ""),
                               placeholder="Flavour text or lore…", height=70,
                               key=f"{key_prefix}_desc")

    st.markdown("**Combat Stats**")
    sc1, sc2, sc3 = st.columns(3)
    with sc1:
        max_health = st.number_input("Max Health", value=float(prefill.get("maxHealth", 10.0)),
                                     min_value=1.0, step=1.0, key=f"{key_prefix}_hp")
        dmg_col1, dmg_col2 = st.columns(2)
        with dmg_col1:
            attack_damage_min = st.number_input("DMG Min", value=int(prefill.get("attackDamageMin", 1)),
                                                min_value=1, step=1, key=f"{key_prefix}_dmg_min")
        with dmg_col2:
            attack_damage_max = st.number_input("DMG Max", value=int(prefill.get("attackDamageMax", 2)),
                                                min_value=1, step=1, key=f"{key_prefix}_dmg_max")
        if attack_damage_max < attack_damage_min:
            st.warning("DMG Max must be ≥ DMG Min.")
            attack_damage_max = attack_damage_min
    with sc2:
        move_speed      = st.number_input("Move Speed",      value=float(prefill.get("moveSpeed", 3.0)),
                                          min_value=0.1, step=0.1, key=f"{key_prefix}_spd")
        attack_cooldown = st.number_input("Attack Cooldown", value=float(prefill.get("attackCooldown", 1.5)),
                                          min_value=0.1, step=0.1, key=f"{key_prefix}_cd")
    with sc3:
        attack_range    = st.number_input("Attack Range",    value=float(prefill.get("attackRange", 2.0)),
                                          min_value=0.5, step=0.5, key=f"{key_prefix}_atkrng")
        detection_range = st.number_input("Detection Range", value=float(prefill.get("detectionRange", 10.0)),
                                          min_value=1.0, step=1.0, key=f"{key_prefix}_detrng")

    st.markdown("**Rotation**")
    rr1, rr2 = st.columns(2)
    with rr1:
        angular_speed  = st.number_input("Angular Speed (chase, deg/s)",
                                         value=float(prefill.get("angularSpeed", 200.0)),
                                         min_value=1.0, step=10.0, key=f"{key_prefix}_ang")
    with rr2:
        rotation_speed = st.number_input("Rotation Speed (attack, deg/s)",
                                         value=float(prefill.get("rotationSpeed", 200.0)),
                                         min_value=1.0, step=10.0, key=f"{key_prefix}_rot")

    st.markdown("**Stun & Misc**")
    sm1, sm2, sm3 = st.columns(3)
    with sm1:
        attack_stun_chance   = st.slider("Stun Chance", min_value=0.0, max_value=1.0,
                                         value=float(prefill.get("attackStunChance", 0.2)),
                                         step=0.05, key=f"{key_prefix}_stunc")
    with sm2:
        attack_stun_duration = st.number_input("Stun Duration",
                                               value=float(prefill.get("attackStunDuration", 1.0)),
                                               min_value=0.0, step=0.1, key=f"{key_prefix}_stund")
    with sm3:
        retarget_interval    = st.number_input("Retarget Interval",
                                               value=float(prefill.get("retargetInterval", 1.0)),
                                               min_value=0.1, step=0.1, key=f"{key_prefix}_retgt")

    st.markdown("**Rewards**")
    rw1, rw2, rw3 = st.columns(3)
    with rw1:
        xp_reward        = st.number_input("XP Reward", value=int(prefill.get("xpReward", 50)),
                                           min_value=0, step=5, key=f"{key_prefix}_xp")
    with rw2:
        give_hp_on_death = st.checkbox("Give HP on Death",
                                       value=bool(prefill.get("giveHPOnDeath", False)),
                                       key=f"{key_prefix}_givehp")
    with rw3:
        hp_reward_on_death = st.number_input("HP Reward",
                                             value=int(prefill.get("hpRewardOnDeath", 1)),
                                             min_value=0, step=1, key=f"{key_prefix}_hprw")

    return {
        "asset_name":          asset_name,
        "enemy_name":          enemy_name or asset_name,
        "description":         description,
        "creature_types":      creature_types,
        "category":            category,
        "level":               level,
        "max_health":          max_health,
        "move_speed":          move_speed,
        "attack_damage_min":   attack_damage_min,
        "attack_damage_max":   attack_damage_max,
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
    }


# ─── Edit Panel ───
# Hidden by default. Opened by clicking an "Edit" button in the library table.
# Closes itself on Save or Cancel, clearing widget state so a fresh prefill loads next time.
def _render_edit_panel(enemies: list[dict]) -> None:
    target = st.session_state.get("enemy_editing")
    if not target:
        return
    prefill = next((e for e in enemies if e["asset_file"] == target), None)
    if prefill is None:
        st.session_state["enemy_editing"] = None
        return

    st.divider()
    st.subheader(f"Edit: {target}")
    with st.form(f"edit_enemy_{target}"):
        form_data = _render_enemy_form(prefill, key_prefix=f"enemy_edit_{target}",
                                       lock_asset_name=True)
        bc1, bc2, _ = st.columns([1, 1, 4])
        save = bc1.form_submit_button("Save Changes", type="primary")
        cancel = bc2.form_submit_button("Cancel")

    if save:
        ok, msg = asset_io.update_enemy_asset(target, form_data)
        if ok:
            st.session_state["enemy_editing"] = None
            _clear_edit_widget_state(target)
            st.cache_data.clear()
            st.success(msg)
            st.rerun()
        else:
            st.error(msg)
    elif cancel:
        st.session_state["enemy_editing"] = None
        _clear_edit_widget_state(target)
        st.rerun()


# ─── Create-Enemy Tab ───
def _render_create_enemy_tab() -> None:
    with st.form("create_enemy", clear_on_submit=True):
        form_data = _render_enemy_form({"asset_file": ""}, key_prefix="enemy_create",
                                       lock_asset_name=False)
        submitted = st.form_submit_button("Create Enemy", type="primary")
    if submitted:
        ok, msg = asset_io.write_enemy_asset(form_data)
        if ok:
            st.success(msg)
            st.cache_data.clear()
        else:
            st.error(msg)


# ─── Create-Enum Tab ───
# Lets the user APPEND new EnemyCategory or CreatureType values to EnemyData.cs.
# Append-only is enforced: enum values map positionally to ints in .asset YAML,
# so deletion/reorder would silently break every existing enemy reference.
def _render_create_enum_tab() -> None:
    cs_path = config.PROJECT_ROOT / "Assets/Scripts/EnemyData.cs"

    if st.session_state.get("enum_added_message"):
        st.warning(
            f"{st.session_state['enum_added_message']}  \n"
            "Click **Restart Server** at the top of the page to load the new value."
        )

    st.caption(
        "Append a new value to one of the EnemyData enums. **Append-only** — existing values "
        "are preserved so .asset references stay intact. Deleting or renaming must be done in Unity."
    )

    cat_col, type_col = st.columns(2)

    with cat_col:
        st.markdown("**Enemy Category**")
        st.caption("Existing:")
        for v in config.ENEMY_CATEGORY.values():
            st.markdown(f"&nbsp;&nbsp;{_category_badge(v)}", unsafe_allow_html=True)
        new_cat = st.text_input("New category name", placeholder="e.g. Champion",
                                key="new_category_name", label_visibility="visible")
        st.caption("Display color must be added manually to `ENEMY_CATEGORY_COLORS` in `dashboard/config.py` — new categories show grey by default.")
        if st.button("Add Category", type="primary", key="add_category_btn"):
            name = (new_cat or "").strip()
            if not name:
                st.error("Name is required.")
            else:
                ok, msg = asset_io.append_enum_value(cs_path, "EnemyCategory", name)
                if ok:
                    st.session_state["enum_added_message"] = msg
                    st.session_state["new_category_name"] = ""
                    st.rerun()
                else:
                    st.error(msg)

    with type_col:
        st.markdown("**Creature Type**")
        st.caption("Existing:")
        for v in config.CREATURE_TYPE.values():
            st.markdown(f"&nbsp;&nbsp;• {v}")
        new_type = st.text_input("New creature type", placeholder="e.g. Demon",
                                 key="new_creature_name", label_visibility="visible")
        if st.button("Add Creature Type", type="primary", key="add_creature_btn"):
            name = (new_type or "").strip()
            if not name:
                st.error("Name is required.")
            else:
                ok, msg = asset_io.append_enum_value(cs_path, "CreatureType", name)
                if ok:
                    st.session_state["enum_added_message"] = msg
                    st.session_state["new_creature_name"] = ""
                    st.rerun()
                else:
                    st.error(msg)


def render():
    st.markdown("""
<style>
div[data-testid="stHorizontalBlock"] div[data-testid="column"]:last-child
    div[data-testid="stButton"] > button {
    padding: 2px 12px;
    min-height: 0;
    height: auto;
    font-size: 0.80em;
    line-height: 1.4;
}
</style>
""", unsafe_allow_html=True)

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
        weights = [2.2, 1.4, 0.7, 1.4, 0.9, 1.0, 0.8, 0.9]
        headers = ["Name", "Category", "Level", "Type", "HP", "DMG", "XP", ""]
        hcols = st.columns(weights, gap="small")
        for hc, label in zip(hcols, headers):
            hc.markdown(f"**{label}**")
        st.markdown("<hr style='margin:2px 0;border-color:#444'>",
                    unsafe_allow_html=True)
        for e in enemies:
            cols = st.columns(weights, gap="small")
            cols[0].write(e["enemyName"])
            cols[1].markdown(_category_badge(e["category"]),
                             unsafe_allow_html=True)
            cols[2].markdown(str(e.get("level", 1)))
            cols[3].write(", ".join(e["creatureTypes"]))
            cols[4].markdown(str(int(e["maxHealth"])))
            cols[5].write(f"{e['attackDamageMin']}–{e['attackDamageMax']}")
            cols[6].markdown(str(e["xpReward"]))
            if cols[7].button("Edit", key=f"edit_btn_{e['asset_file']}"):
                _clear_edit_widget_state(e["asset_file"])
                st.session_state["enemy_editing"] = e["asset_file"]
                st.rerun()

    # ─── Edit Panel (conditional) ───
    _render_edit_panel(enemies)

    st.divider()

    # ─── Create Tabs ───
    create_enemy_tab, create_enum_tab = st.tabs(["Create Enemy", "Create Category / Type"])
    with create_enemy_tab:
        _render_create_enemy_tab()
    with create_enum_tab:
        _render_create_enum_tab()
