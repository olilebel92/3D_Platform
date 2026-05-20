import pandas as pd
import streamlit as st

import asset_io
import config


def _stat_lines_str(stat_lines: list[dict]) -> str:
    if not stat_lines:
        return "—"
    parts = []
    for sl in stat_lines:
        v = sl["value"]
        v_str = f"{int(v)}" if v == int(v) else f"{v}"
        parts.append(f"+{v_str} {sl['type']}")
    return ", ".join(parts)


def _rarity_badge(rarity: str) -> str:
    color = config.RARITY_COLORS.get(rarity, "#CCCCCC")
    return f'<span style="color:{color};font-weight:bold">{rarity}</span>'


@st.cache_data
def _load_items():
    return asset_io.scan_items()


@st.cache_data
def _load_rarities():
    return asset_io.scan_rarities()


@st.cache_data
def _load_main_types():
    return asset_io.scan_main_types()


@st.cache_data
def _load_sub_types():
    return asset_io.scan_sub_types()


def _hex_to_rgb(hex_str: str) -> dict:
    """'#1EFF00' → {r,g,b,a} floats in [0,1]. Falls back to white on bad input."""
    h = (hex_str or "").lstrip("#")
    if len(h) != 6:
        return {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}
    try:
        return {
            "r": int(h[0:2], 16) / 255.0,
            "g": int(h[2:4], 16) / 255.0,
            "b": int(h[4:6], 16) / 255.0,
            "a": 1.0,
        }
    except ValueError:
        return {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}


def _rgb_to_hex(c: dict) -> str:
    """{r,g,b,a} floats → '#RRGGBB'."""
    def chan(v): return max(0, min(255, int(round(float(v) * 255))))
    return f"#{chan(c.get('r', 1)):02X}{chan(c.get('g', 1)):02X}{chan(c.get('b', 1)):02X}"


def _clear_edit_widget_state(asset_file: str) -> None:
    """Drop any persisted widget state for the edit form so a fresh prefill applies."""
    prefix = f"item_edit_{asset_file}_"
    for k in [k for k in list(st.session_state.keys()) if k.startswith(prefix)]:
        del st.session_state[k]


# ─── Form Renderer ───
# Shared by both "Create Item" and "Edit Item". `prefill` carries default values;
# `key_prefix` namespaces widget keys so create and edit forms don't collide. The
# asset filename is editable only on create — locked on edit (file rename is out of scope).
def _render_item_form(prefill: dict, key_prefix: str, lock_asset_name: bool) -> dict:
    col1, col2 = st.columns(2)
    with col1:
        if lock_asset_name:
            st.text_input("Asset File Name", value=prefill["asset_file"], disabled=True,
                          key=f"{key_prefix}_asset")
            asset_name = prefill["asset_file"]
        else:
            asset_name = st.text_input("Asset File Name", placeholder="e.g. VoidGauntlets",
                                       key=f"{key_prefix}_asset")
        item_name = st.text_input("Display Name", value=prefill.get("itemName", ""),
                                  placeholder="e.g. Gauntlets of the Void",
                                  key=f"{key_prefix}_name")
    with col2:
        slot_options   = list(config.EQUIPMENT_SLOT.values())
        rarity_options = list(config.ITEM_RARITY.values())
        slot = st.selectbox("Slot", slot_options,
            index=slot_options.index(prefill.get("slot", slot_options[0]))
                  if prefill.get("slot") in slot_options else 0,
            key=f"{key_prefix}_slot")
        rarity = st.selectbox("Rarity", rarity_options,
            index=rarity_options.index(prefill.get("rarity", rarity_options[0]))
                  if prefill.get("rarity") in rarity_options else 0,
            key=f"{key_prefix}_rarity")

    description = st.text_area("Description", value=prefill.get("description", ""),
                               placeholder="Flavour text or tooltip…", height=80,
                               key=f"{key_prefix}_desc")

    st.markdown("**Stat Lines** — add one row per stat bonus")
    existing_stats = prefill.get("statLines") or []
    if existing_stats:
        default_stats = pd.DataFrame({
            "type":  pd.Series([sl["type"]  for sl in existing_stats], dtype=str),
            "value": pd.Series([sl["value"] for sl in existing_stats], dtype=float),
        })
    else:
        default_stats = pd.DataFrame({
            "type":  pd.Series(["STR"], dtype=str),
            "value": pd.Series([10.0],  dtype=float),
        })
    stat_df = st.data_editor(
        default_stats,
        num_rows="dynamic",
        column_config={
            "type":  st.column_config.SelectboxColumn(
                "Stat Type", options=list(config.STAT_TYPE.values()), required=True
            ),
            "value": st.column_config.NumberColumn("Value", min_value=0.0, step=1.0),
        },
        use_container_width=True,
        key=f"{key_prefix}_stats",
    )

    stat_lines = [
        {"type": row["type"], "value": float(row["value"])}
        for _, row in stat_df.iterrows()
        if row["type"] and row["value"] != 0
    ]

    return {
        "asset_name":  asset_name,
        "item_name":   item_name or asset_name,
        "description": description,
        "slot":        slot,
        "rarity":      rarity,
        "stat_lines":  stat_lines,
    }


# ─── Edit Panel ───
# Hidden by default. Opened by clicking an "Edit" button in the library table.
# Closes itself on Save or Cancel, clearing widget state so a fresh prefill loads next time.
def _render_edit_panel(items: list[dict]) -> None:
    target = st.session_state.get("item_editing")
    if not target:
        return
    prefill = next((it for it in items if it["asset_file"] == target), None)
    if prefill is None:
        st.session_state["item_editing"] = None
        return

    st.divider()
    st.subheader(f"Edit: {target}")
    with st.form(f"edit_item_{target}"):
        form_data = _render_item_form(prefill, key_prefix=f"item_edit_{target}",
                                      lock_asset_name=True)
        bc1, bc2, _ = st.columns([1, 1, 4])
        save = bc1.form_submit_button("Save Changes", type="primary")
        cancel = bc2.form_submit_button("Cancel")

    if save:
        ok, msg = asset_io.update_item_asset(target, form_data)
        if ok:
            st.session_state["item_editing"] = None
            _clear_edit_widget_state(target)
            st.cache_data.clear()
            st.success(msg)
            st.rerun()
        else:
            st.error(msg)
    elif cancel:
        st.session_state["item_editing"] = None
        _clear_edit_widget_state(target)
        st.rerun()


def render():
    col_left, col_right = st.columns([3, 1])
    with col_right:
        if st.button("Refresh Items", key="refresh_items"):
            st.cache_data.clear()
            st.rerun()

    items = _load_items()

    # ─── Library Table ───
    st.subheader(f"Items ({len(items)})")
    if not items:
        st.info("No ItemData assets found. Create one below.")
    else:
        weights = [2.0, 2.4, 1.0, 1.2, 3.0, 0.8]
        headers = ["Asset File", "Item Name", "Slot", "Rarity", "Stats", ""]
        hcols = st.columns(weights, gap="small")
        for hc, label in zip(hcols, headers):
            hc.markdown(f"**{label}**")
        st.markdown("<hr style='margin:2px 0;border-color:#444'>",
                    unsafe_allow_html=True)
        for it in items:
            cols = st.columns(weights, gap="small")
            cols[0].write(it["asset_file"])
            cols[1].write(it["itemName"])
            cols[2].write(it["slot"])
            cols[3].markdown(_rarity_badge(it["rarity"]), unsafe_allow_html=True)
            cols[4].markdown(f"<span style='font-size:0.85em'>{_stat_lines_str(it['statLines'])}</span>",
                             unsafe_allow_html=True)
            if cols[5].button("Edit", key=f"item_edit_btn_{it['asset_file']}"):
                _clear_edit_widget_state(it["asset_file"])
                st.session_state["item_editing"] = it["asset_file"]
                st.rerun()

    # ─── Edit Panel (conditional) ───
    _render_edit_panel(items)

    st.divider()

    # ─── Create Tabs ───
    create_item_tab, create_rarity_tab, create_main_tab, create_sub_tab = st.tabs(
        ["Create Item", "Create Rarity", "Create Main Type", "Create Sub Type"]
    )
    with create_item_tab:
        _render_create_item_tab()
    with create_rarity_tab:
        _render_create_rarity_tab()
    with create_main_tab:
        _render_create_main_type_tab()
    with create_sub_tab:
        _render_create_sub_type_tab()


# ─── Create-Item Tab ───
def _render_create_item_tab() -> None:
    with st.form("create_item", clear_on_submit=True):
        form_data = _render_item_form({"asset_file": ""}, key_prefix="item_create",
                                      lock_asset_name=False)
        submitted = st.form_submit_button("Create Item", type="primary")

    if submitted:
        ok, msg = asset_io.write_item_asset(form_data)
        if ok:
            st.success(msg)
            st.cache_data.clear()
        else:
            st.error(msg)


# ─── Rarity Form Factory ───
# Shared by create + edit. `prefill` carries default values; `key_prefix` namespaces widgets.
def _render_rarity_form(prefill: dict, key_prefix: str, lock_asset_name: bool) -> dict:
    col1, col2 = st.columns(2)
    with col1:
        if lock_asset_name:
            st.text_input("Asset File Name", value=prefill["asset_file"], disabled=True,
                          key=f"{key_prefix}_asset")
            asset_name = prefill["asset_file"]
        else:
            asset_name = st.text_input("Asset File Name", placeholder="e.g. Mythic",
                                       key=f"{key_prefix}_asset")
        display_name = st.text_input("Display Name", value=prefill.get("displayName", ""),
                                     placeholder="e.g. Mythic",
                                     key=f"{key_prefix}_name")
        sort_order = st.number_input("Sort Order", value=int(prefill.get("sortOrder", 0)),
                                     min_value=0, step=1, key=f"{key_prefix}_sort",
                                     help="Lower = more common. Used by filter sorts and 'is higher than' comparisons.")
    with col2:
        default_hex = _rgb_to_hex(prefill["color"]) if prefill.get("color") else "#CCCCCC"
        color_hex = st.color_picker("Color", value=default_hex, key=f"{key_prefix}_color",
                                    help="Nameplate / UI tint. Glow color is set to the same value.")
        glow_intensity = st.number_input("Glow Intensity",
                                         value=float(prefill.get("glowIntensity", 1.5)),
                                         min_value=0.0, step=0.1, key=f"{key_prefix}_glow")

    st.markdown("**Drop Tuning**")
    dc1, dc2 = st.columns(2)
    with dc1:
        drop_weight = st.number_input("Drop Weight",
                                      value=float(prefill.get("dropWeight", 1.0)),
                                      min_value=0.0, step=0.1, key=f"{key_prefix}_drop",
                                      help="Relative weight. Higher = more common.")
    with dc2:
        wave_unlock = st.number_input("Wave Unlock Threshold",
                                      value=int(prefill.get("waveUnlockThreshold", 1)),
                                      min_value=1, step=1, key=f"{key_prefix}_wave",
                                      help="Earliest wave this rarity can drop.")

    st.markdown("**Stat Rolls**")
    sc1, sc2, sc3 = st.columns(3)
    with sc1:
        stat_min = st.number_input("Stat Lines Min",
                                   value=int(prefill.get("statLineCountMin", 1)),
                                   min_value=0, step=1, key=f"{key_prefix}_smin")
    with sc2:
        stat_max = st.number_input("Stat Lines Max",
                                   value=int(prefill.get("statLineCountMax", 2)),
                                   min_value=0, step=1, key=f"{key_prefix}_smax")
    with sc3:
        stat_mult = st.number_input("Stat Multiplier",
                                    value=float(prefill.get("statValueMultiplier", 1.0)),
                                    min_value=0.0, step=0.25, key=f"{key_prefix}_smult",
                                    help="Multiplier applied to all rolled stat values. Higher rarities should use 1.5–3x.")

    stat_options = list(config.STAT_TYPE.values())
    prefill_banned = [s for s in (prefill.get("bannedStats") or []) if s in stat_options]
    banned_stats = st.multiselect("Banned Stats", stat_options,
                                  default=prefill_banned,
                                  key=f"{key_prefix}_banned",
                                  help="Stats that never roll on items of this rarity. E.g. ban HP/Regen on Common.")

    return {
        "asset_name":     asset_name,
        "display_name":   display_name or asset_name,
        "sort_order":     sort_order,
        "color":          _hex_to_rgb(color_hex),
        "glow_color":     _hex_to_rgb(color_hex),
        "glow_intensity": glow_intensity,
        "drop_weight":    drop_weight,
        "wave_unlock":    wave_unlock,
        "stat_min":       stat_min,
        "stat_max":       stat_max,
        "stat_mult":      stat_mult,
        "banned_stats":   banned_stats,
    }


def _clear_rarity_edit_widget_state(asset_file: str) -> None:
    prefix = f"rarity_edit_{asset_file}_"
    for k in [k for k in list(st.session_state.keys()) if k.startswith(prefix)]:
        del st.session_state[k]


def _render_rarity_edit_panel(rarities: list[dict]) -> None:
    target = st.session_state.get("rarity_editing")
    if not target:
        return
    prefill = next((r for r in rarities if r["asset_file"] == target), None)
    if prefill is None:
        st.session_state["rarity_editing"] = None
        return

    st.divider()
    st.subheader(f"Edit: {target}")
    with st.form(f"edit_rarity_{target}"):
        form_data = _render_rarity_form(prefill, key_prefix=f"rarity_edit_{target}",
                                        lock_asset_name=True)
        bc1, bc2, _ = st.columns([1, 1, 4])
        save = bc1.form_submit_button("Save Changes", type="primary")
        cancel = bc2.form_submit_button("Cancel")

    if save:
        if form_data["stat_max"] < form_data["stat_min"]:
            st.error("Stat Lines Max must be ≥ Stat Lines Min.")
            return
        ok, msg = asset_io.update_rarity_asset(target, form_data)
        if ok:
            st.session_state["rarity_editing"] = None
            _clear_rarity_edit_widget_state(target)
            st.cache_data.clear()
            st.success(msg)
            st.rerun()
        else:
            st.error(msg)
    elif cancel:
        st.session_state["rarity_editing"] = None
        _clear_rarity_edit_widget_state(target)
        st.rerun()


# ─── Create-Rarity Tab ───
def _render_create_rarity_tab() -> None:
    rarities = _load_rarities()

    st.caption(
        "RarityData assets under `Assets/ScriptableObjects/Items/Rarities/`. "
        "Each defines colors, glow, drop weight, wave unlock, and stat-roll ranges. "
        "After creating one, drag it into `ItemGenerator.rarities` in the Unity Inspector."
    )

    st.markdown("**Existing rarities**")
    if not rarities:
        st.caption("No rarities found yet.")
    else:
        weights = [0.4, 1.4, 1.4, 0.7, 0.7, 0.9, 0.7, 2.0, 0.6]
        headers = ["#", "Name", "Color", "Drop", "Wave", "Stats", "Mult", "Banned Stats", ""]
        hcols = st.columns(weights, gap="small")
        for hc, label in zip(hcols, headers):
            hc.markdown(f"**{label}**")
        st.markdown("<hr style='margin:2px 0;border-color:#444'>", unsafe_allow_html=True)
        for r in rarities:
            cols = st.columns(weights, gap="small")
            hex_col = _rgb_to_hex(r["color"])
            cols[0].write(r["sortOrder"])
            cols[1].markdown(f"<span style='color:{hex_col};font-weight:bold'>{r['displayName']}</span>",
                             unsafe_allow_html=True)
            cols[2].markdown(
                f"<span style='display:inline-block;width:14px;height:14px;"
                f"background:{hex_col};border:1px solid #555;vertical-align:middle'></span> "
                f"<code style='font-size:0.85em'>{hex_col}</code>",
                unsafe_allow_html=True,
            )
            cols[3].write(f"{r['dropWeight']:g}")
            cols[4].write(r["waveUnlockThreshold"])
            cols[5].write(f"{r['statLineCountMin']}–{r['statLineCountMax']}")
            cols[6].write(f"×{r['statValueMultiplier']:g}")
            cols[7].write(", ".join(r["bannedStats"]) or "—")
            if cols[8].button("Edit", key=f"rarity_edit_btn_{r['asset_file']}"):
                _clear_rarity_edit_widget_state(r["asset_file"])
                st.session_state["rarity_editing"] = r["asset_file"]
                st.rerun()

    _render_rarity_edit_panel(rarities)

    st.markdown("&nbsp;", unsafe_allow_html=True)
    st.markdown("**Create new rarity**")

    next_sort_order = (max((r["sortOrder"] for r in rarities), default=-1) + 1)

    with st.form("create_rarity", clear_on_submit=True):
        form_data = _render_rarity_form(
            {"asset_file": "", "sortOrder": next_sort_order, "glowIntensity": 1.5,
             "dropWeight": 1.0, "waveUnlockThreshold": 1,
             "statLineCountMin": 1, "statLineCountMax": 2, "statValueMultiplier": 1.0},
            key_prefix="rarity_create", lock_asset_name=False,
        )
        submitted = st.form_submit_button("Create Rarity", type="primary")

    if submitted:
        if form_data["stat_max"] < form_data["stat_min"]:
            st.error("Stat Lines Max must be ≥ Stat Lines Min.")
            return
        ok, msg = asset_io.write_rarity_asset(form_data)
        if ok:
            st.success(msg)
            st.cache_data.clear()
            st.rerun()
        else:
            st.error(msg)


# ─── MainType Form Factory ───
def _render_main_type_form(prefill: dict, key_prefix: str, lock_asset_name: bool) -> dict:
    c1, c2 = st.columns(2)
    with c1:
        if lock_asset_name:
            st.text_input("Asset File Name", value=prefill["asset_file"], disabled=True,
                          key=f"{key_prefix}_asset")
            asset_name = prefill["asset_file"]
        else:
            asset_name = st.text_input("Asset File Name", placeholder="e.g. Trinket",
                                       key=f"{key_prefix}_asset")
        display_name = st.text_input("Display Name", value=prefill.get("displayName", ""),
                                     placeholder="e.g. Trinket",
                                     key=f"{key_prefix}_name")
    with c2:
        is_weapon = st.checkbox("Is Weapon",
                                value=bool(prefill.get("isWeapon", False)),
                                key=f"{key_prefix}_isweapon",
                                help="Tick for weapon categories — items will be created as WeaponItemData.")
    return {
        "asset_name":   asset_name,
        "display_name": display_name or asset_name,
        "is_weapon":    is_weapon,
    }


def _clear_main_type_edit_widget_state(asset_file: str) -> None:
    prefix = f"maintype_edit_{asset_file}_"
    for k in [k for k in list(st.session_state.keys()) if k.startswith(prefix)]:
        del st.session_state[k]


def _render_main_type_edit_panel(main_types: list[dict]) -> None:
    target = st.session_state.get("main_type_editing")
    if not target:
        return
    prefill = next((m for m in main_types if m["asset_file"] == target), None)
    if prefill is None:
        st.session_state["main_type_editing"] = None
        return

    st.divider()
    st.subheader(f"Edit: {target}")
    with st.form(f"edit_maintype_{target}"):
        form_data = _render_main_type_form(prefill, key_prefix=f"maintype_edit_{target}",
                                           lock_asset_name=True)
        bc1, bc2, _ = st.columns([1, 1, 4])
        save = bc1.form_submit_button("Save Changes", type="primary")
        cancel = bc2.form_submit_button("Cancel")

    if save:
        ok, msg = asset_io.update_main_type_asset(target, form_data)
        if ok:
            st.session_state["main_type_editing"] = None
            _clear_main_type_edit_widget_state(target)
            st.cache_data.clear()
            st.success(msg)
            st.rerun()
        else:
            st.error(msg)
    elif cancel:
        st.session_state["main_type_editing"] = None
        _clear_main_type_edit_widget_state(target)
        st.rerun()


# ─── Create-MainType Tab ───
def _render_create_main_type_tab() -> None:
    main_types = _load_main_types()

    st.caption(
        "MainTypeData assets under `Assets/ScriptableObjects/Items/MainTypes/`. "
        "Top-level item categories (Weapon, Armor, …). SubTypes reference these."
    )

    st.markdown("**Existing main types**")
    if not main_types:
        st.caption("No main types found yet.")
    else:
        weights = [2.2, 1.2, 1.0, 0.7]
        headers = ["Name", "Is Weapon", "GUID", ""]
        hcols = st.columns(weights, gap="small")
        for hc, label in zip(hcols, headers):
            hc.markdown(f"**{label}**")
        st.markdown("<hr style='margin:2px 0;border-color:#444'>", unsafe_allow_html=True)
        for mt in main_types:
            cols = st.columns(weights, gap="small")
            cols[0].markdown(f"**{mt['displayName']}**")
            cols[1].write("Yes" if mt["isWeapon"] else "No")
            cols[2].markdown(f"<code style='font-size:0.8em'>{(mt['guid'] or '—')[:12]}…</code>",
                             unsafe_allow_html=True)
            if cols[3].button("Edit", key=f"maintype_edit_btn_{mt['asset_file']}"):
                _clear_main_type_edit_widget_state(mt["asset_file"])
                st.session_state["main_type_editing"] = mt["asset_file"]
                st.rerun()

    _render_main_type_edit_panel(main_types)

    st.markdown("&nbsp;", unsafe_allow_html=True)
    st.markdown("**Create new main type**")

    with st.form("create_main_type", clear_on_submit=True):
        form_data = _render_main_type_form({"asset_file": ""}, key_prefix="maintype_create",
                                           lock_asset_name=False)
        submitted = st.form_submit_button("Create Main Type", type="primary")

    if submitted:
        ok, msg = asset_io.write_main_type_asset(form_data)
        if ok:
            st.success(msg)
            st.cache_data.clear()
            st.rerun()
        else:
            st.error(msg)


# ─── SubType Form Factory ───
# Requires `main_types` so the MainType selectbox can pull labels.
def _render_sub_type_form(prefill: dict, key_prefix: str, lock_asset_name: bool,
                          main_types: list[dict]) -> dict:
    mt_labels = [mt["displayName"] for mt in main_types]
    prefill_mt = prefill.get("mainTypeName", mt_labels[0] if mt_labels else "")

    c1, c2 = st.columns(2)
    with c1:
        if lock_asset_name:
            st.text_input("Asset File Name", value=prefill["asset_file"], disabled=True,
                          key=f"{key_prefix}_asset")
            asset_name = prefill["asset_file"]
        else:
            asset_name = st.text_input("Asset File Name", placeholder="e.g. Gloves",
                                       key=f"{key_prefix}_asset")
        display_name = st.text_input("Display Name", value=prefill.get("displayName", ""),
                                     placeholder="e.g. Gloves",
                                     key=f"{key_prefix}_name")
        main_type_label = st.selectbox(
            "Main Type", mt_labels,
            index=mt_labels.index(prefill_mt) if prefill_mt in mt_labels else 0,
            key=f"{key_prefix}_main",
        )
    with c2:
        slot_options = list(config.EQUIPMENT_SLOT.values())
        prefill_slot = prefill.get("equipSlot", slot_options[0])
        equip_slot = st.selectbox(
            "Equip Slot", slot_options,
            index=slot_options.index(prefill_slot) if prefill_slot in slot_options else 0,
            key=f"{key_prefix}_slot",
        )
        attach_bone = st.text_input("Attach Bone (optional)",
                                    value=prefill.get("attachBone", ""),
                                    placeholder="e.g. RightHand",
                                    key=f"{key_prefix}_bone",
                                    help="Bone/attach point name on the player rig. Empty = use slot default.")

    stat_options = list(config.STAT_TYPE.values())
    prefill_allowed  = [s for s in (prefill.get("allowedStats")  or []) if s in stat_options]
    prefill_reserved = [s for s in (prefill.get("reservedStats") or []) if s in stat_options]
    sc1, sc2 = st.columns(2)
    with sc1:
        allowed_stats = st.multiselect("Allowed Stats", stat_options,
                                       default=prefill_allowed,
                                       key=f"{key_prefix}_allowed",
                                       help="Stats the generator may roll. Leave empty to allow ALL stats.")
    with sc2:
        reserved_stats = st.multiselect("Reserved Stats", stat_options,
                                        default=prefill_reserved,
                                        key=f"{key_prefix}_reserved",
                                        help="Stats exclusive to this subtype — other subtypes cannot roll them, "
                                             "and this subtype rolls them even if not in Allowed Stats. "
                                             "Example: put MovementSpeed in Boots.reservedStats.")

    default_materials = ", ".join(prefill.get("nameMaterials") or []) \
        or "Iron, Steel, Shadow, Storm, Mystic, Dragon"
    materials_str = st.text_input("Name Materials (comma-separated)",
                                  value=default_materials,
                                  key=f"{key_prefix}_materials",
                                  help="Name parts the generator may use when building item names.")
    materials = [m.strip() for m in (materials_str or "").split(",") if m.strip()]

    mt_by_label = {mt["displayName"]: mt for mt in main_types}
    selected_mt = mt_by_label.get(main_type_label)

    return {
        "asset_name":     asset_name,
        "display_name":   display_name or asset_name,
        "main_type_label": main_type_label,
        "main_type_guid": selected_mt["guid"] if selected_mt else None,
        "equip_slot":     equip_slot,
        "attach_bone":    attach_bone,
        "allowed_stats":  allowed_stats,
        "reserved_stats": reserved_stats,
        "name_materials": materials,
    }


def _clear_sub_type_edit_widget_state(asset_file: str) -> None:
    prefix = f"subtype_edit_{asset_file}_"
    for k in [k for k in list(st.session_state.keys()) if k.startswith(prefix)]:
        del st.session_state[k]


def _render_sub_type_edit_panel(sub_types: list[dict], main_types: list[dict]) -> None:
    target = st.session_state.get("sub_type_editing")
    if not target:
        return
    prefill = next((s for s in sub_types if s["asset_file"] == target), None)
    if prefill is None:
        st.session_state["sub_type_editing"] = None
        return

    st.divider()
    st.subheader(f"Edit: {target}")
    with st.form(f"edit_subtype_{target}"):
        form_data = _render_sub_type_form(prefill, key_prefix=f"subtype_edit_{target}",
                                          lock_asset_name=True, main_types=main_types)
        bc1, bc2, _ = st.columns([1, 1, 4])
        save = bc1.form_submit_button("Save Changes", type="primary")
        cancel = bc2.form_submit_button("Cancel")

    if save:
        if not form_data.get("main_type_guid"):
            st.error(f"MainType '{form_data['main_type_label']}' has no GUID — re-import in Unity first.")
            return
        ok, msg = asset_io.update_sub_type_asset(target, form_data)
        if ok:
            st.session_state["sub_type_editing"] = None
            _clear_sub_type_edit_widget_state(target)
            st.cache_data.clear()
            st.success(msg)
            st.rerun()
        else:
            st.error(msg)
    elif cancel:
        st.session_state["sub_type_editing"] = None
        _clear_sub_type_edit_widget_state(target)
        st.rerun()


# ─── Create-SubType Tab ───
def _render_create_sub_type_tab() -> None:
    main_types = _load_main_types()
    sub_types  = _load_sub_types()

    st.caption(
        "SubTypeData assets under `Assets/ScriptableObjects/Items/SubTypes/`. "
        "Specific item kinds (Helm, Sword, …) under a MainType. After creating one, "
        "drag it into `ItemGenerator.subTypes` in the Unity Inspector."
    )

    st.markdown("**Existing sub types**")
    if not sub_types:
        st.caption("No sub types found yet.")
    else:
        weights = [1.4, 1.2, 1.0, 1.6, 1.6, 0.6]
        headers = ["Name", "Main Type", "Slot", "Allowed Stats", "Reserved Stats", ""]
        hcols = st.columns(weights, gap="small")
        for hc, label in zip(hcols, headers):
            hc.markdown(f"**{label}**")
        st.markdown("<hr style='margin:2px 0;border-color:#444'>", unsafe_allow_html=True)
        for s in sub_types:
            cols = st.columns(weights, gap="small")
            cols[0].markdown(f"**{s['displayName']}**")
            cols[1].write(s["mainTypeName"])
            cols[2].write(s["equipSlot"])
            cols[3].write(", ".join(s["allowedStats"]) or "(any)")
            cols[4].write(", ".join(s["reservedStats"]) or "—")
            if cols[5].button("Edit", key=f"subtype_edit_btn_{s['asset_file']}"):
                _clear_sub_type_edit_widget_state(s["asset_file"])
                st.session_state["sub_type_editing"] = s["asset_file"]
                st.rerun()

    _render_sub_type_edit_panel(sub_types, main_types)

    st.markdown("&nbsp;", unsafe_allow_html=True)
    st.markdown("**Create new sub type**")

    if not main_types:
        st.warning("Create a MainType first — SubTypes must reference one.")
        return

    with st.form("create_sub_type", clear_on_submit=True):
        form_data = _render_sub_type_form({"asset_file": ""}, key_prefix="subtype_create",
                                          lock_asset_name=False, main_types=main_types)
        submitted = st.form_submit_button("Create Sub Type", type="primary")

    if submitted:
        if not form_data.get("main_type_guid"):
            st.error(f"MainType '{form_data['main_type_label']}' has no GUID — re-import in Unity first.")
            return
        ok, msg = asset_io.write_sub_type_asset(form_data)
        if ok:
            st.success(msg)
            st.cache_data.clear()
            st.rerun()
        else:
            st.error(msg)
