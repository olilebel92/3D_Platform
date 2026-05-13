import streamlit as st

import items_tab
import spells_tab

st.set_page_config(
    page_title="HackNSLASH Dashboard",
    page_icon="⚔",
    layout="wide",
)

st.title("HackNSLASH — Asset Dashboard")
st.caption("Browse and create ScriptableObjects. Changes are written directly to the Unity project.")

if st.button("Refresh All", help="Re-read all .asset files from disk"):
    st.cache_data.clear()
    st.rerun()

tab_items, tab_spells = st.tabs(["Items", "Spells"])

with tab_items:
    items_tab.render()

with tab_spells:
    spells_tab.render()
