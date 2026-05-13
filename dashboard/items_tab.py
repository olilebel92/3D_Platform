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
        rows = []
        for it in items:
            rows.append({
                "Asset File": it["asset_file"],
                "Item Name":  it["itemName"],
                "Slot":       it["slot"],
                "Rarity":     it["rarity"],
                "Stats":      _stat_lines_str(it["statLines"]),
            })
        df = pd.DataFrame(rows)

        # Render as HTML for rarity coloring
        def rarity_html(val):
            color = config.RARITY_COLORS.get(val, "#CCCCCC")
            return f'<span style="color:{color};font-weight:bold">{val}</span>'

        html_rows = ""
        for _, row in df.iterrows():
            html_rows += (
                f"<tr>"
                f"<td>{row['Asset File']}</td>"
                f"<td>{row['Item Name']}</td>"
                f"<td>{row['Slot']}</td>"
                f"<td>{rarity_html(row['Rarity'])}</td>"
                f"<td style='font-size:0.85em'>{row['Stats']}</td>"
                f"</tr>"
            )

        table_html = f"""
        <table style="width:100%;border-collapse:collapse">
          <thead>
            <tr style="border-bottom:1px solid #444;text-align:left">
              <th style="padding:6px">Asset File</th>
              <th style="padding:6px">Item Name</th>
              <th style="padding:6px">Slot</th>
              <th style="padding:6px">Rarity</th>
              <th style="padding:6px">Stats</th>
            </tr>
          </thead>
          <tbody>{"".join(html_rows.split())}</tbody>
        </table>
        """
        # Re-join rows properly
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
                f"<td style='padding:5px'>{row['Asset File']}</td>"
                f"<td style='padding:5px'>{row['Item Name']}</td>"
                f"<td style='padding:5px'>{row['Slot']}</td>"
                f"<td style='padding:5px'>{rarity_html(row['Rarity'])}</td>"
                f"<td style='padding:5px;font-size:0.85em'>{row['Stats']}</td>"
                f"</tr>"
                for _, row in df.iterrows()
            )
            + "</tbody></table>"
        )
        st.markdown(table_html, unsafe_allow_html=True)

    st.divider()

    # ─── Create Form ───
    st.subheader("Create New Item")
    with st.form("create_item", clear_on_submit=True):
        col1, col2 = st.columns(2)
        with col1:
            asset_name  = st.text_input("Asset File Name", placeholder="e.g. VoidGauntlets")
            item_name   = st.text_input("Display Name",    placeholder="e.g. Gauntlets of the Void")
        with col2:
            slot   = st.selectbox("Slot",   list(config.EQUIPMENT_SLOT.values()))
            rarity = st.selectbox("Rarity", list(config.ITEM_RARITY.values()))

        description = st.text_area("Description", placeholder="Flavour text or tooltip…", height=80)

        st.markdown("**Stat Lines** — add one row per stat bonus")
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
        )

        submitted = st.form_submit_button("Create Item", type="primary")

    if submitted:
        stat_lines = [
            {"type": row["type"], "value": float(row["value"])}
            for _, row in stat_df.iterrows()
            if row["type"] and row["value"] != 0
        ]
        ok, msg = asset_io.write_item_asset({
            "asset_name":  asset_name,
            "item_name":   item_name or asset_name,
            "description": description,
            "slot":        slot,
            "rarity":      rarity,
            "stat_lines":  stat_lines,
        })
        if ok:
            st.success(msg)
            st.cache_data.clear()
        else:
            st.error(msg)
