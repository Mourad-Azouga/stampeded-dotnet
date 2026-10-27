//! The committed C header must continue to match the connector's exports.

#[test]
fn the_committed_header_matches_the_exports() {
    let crate_dir = env!("CARGO_MANIFEST_DIR");
    let config =
        cbindgen::Config::from_file(format!("{crate_dir}/cbindgen.toml")).expect("cbindgen.toml");
    let mut generated = Vec::new();
    cbindgen::Builder::new()
        .with_crate(crate_dir)
        .with_config(config)
        .generate()
        .expect("cbindgen")
        .write(&mut generated);

    assert_eq!(
        std::fs::read(format!("{crate_dir}/include/stampeded.h")).expect("C header"),
        generated,
        "The C header differs from the connector's exports"
    );
}
