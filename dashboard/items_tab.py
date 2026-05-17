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


# ─── Form Renderer ───
# Shared by create + edit. `prefill` supplies defaults; `key_prefix` namespaces widgets.
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
        st.info("No ItemData assets found.")
    else:
        def rarity_html(val):
            color = config.RARITY_COLORS.get(val, "#CCCCCC")
            return f'<span style="color:{color};font-weight:bold">{val}</span>'

        table_html = (
            "<table style='width:100%;border-collapse:collapse'>"
            "<thead><tr style='border-bottom:1px solid #444;text-align:left'>"
            "<th style='padding:6px'>Asset File</th>"
            "<th style='padding:6px'>Item Name</th>"
            "<th style='padding:6px'>Slot</th>"
            "<th style='padding:6px'>Rarity</th>"
            "<th style='padding:6px'>Stats</th>"
            "</tr></thead><tbody>"
            + "".join(
                f"<tr style='border-bottom:1px solid #333'>"
                f"<td style='padding:5px'>{it['asset_file']}</td>"
                f"<td style='padding:5px'>{it['itemName']}</td>"
                f"<td style='padding:5px'>{it['slot']}</td>"
                f"<td style='padding:5px'>{rarity_html(it['rarity'])}</td>"
                f"<td style='padding:5px;font-size:0.85em'>{_stat_lines_str(it['statLines'])}</td>"
                f"</tr>"
                for it in items
            )
            + "</tbody></table>"
        )
        st.markdown(table_html, unsafe_allow_html=True)

    st.divider()

    # ─── Edit Existing ───
    st.subheader("Edit Existing Item")
    if not items:
        st.caption("No items to edit yet.")
    else:
        asset_files = [it["asset_file"] for it in items]
        selected = st.selectbox("Select an item to edit", asset_files, key="item_edit_select")
        prefill = next(it for it in items if it["asset_file"] == selected)

        with st.form(f"edit_item_{selected}"):
            form_data = _render_item_form(prefill, key_prefix=f"item_edit_{selected}",
                                          lock_asset_name=True)
            save = st.form_submit_button("Save Changes", type="primary")

        if save:
            ok, msg = asset_io.update_item_asset(selected, form_data)
            if ok:
                st.success(msg)
                st.cache_data.clear()
                st.rerun()
            else:
                st.error(msg)

    st.divider()

    # ─── Create New ───
    st.subheader("Create New Item")
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
