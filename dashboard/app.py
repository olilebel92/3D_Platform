import importlib
import streamlit as st

import asset_io
import config
import items_tab
import spells_tab
import enemies_tab

st.set_page_config(
    page_title="HackNSLASH Dashboard",
    page_icon="⚔",
    layout="wide",
)

st.title("HackNSLASH — Asset Dashboard")
st.caption("Browse and create ScriptableObjects. Changes are written directly to the Unity project.")

col1, col2 = st.columns([1, 1])
with col1:
    if st.button("Refresh All", help="Re-read all .asset files from disk"):
        st.cache_data.clear()
        st.rerun()
with col2:
    if st.button("Restart Server", help="Reloads all Python modules — use after updating dashboard code"):
        for mod in [config, asset_io, items_tab, spells_tab, enemies_tab]:
            importlib.reload(mod)
        st.cache_data.clear()
        st.rerun()

tab_items, tab_spells, tab_enemies = st.tabs(["Items", "Spells", "Enemies"])

with tab_items:
    items_tab.render()

with tab_spells:
    spells_tab.render()

with tab_enemies:
    enemies_tab.render()
