// Release builds on Windows shouldn't open a console window next to the app.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    old_country_lib::run();
}
