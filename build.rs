use std::io::Write;

fn main() {
    // Only run on Windows
    if std::env::var("CARGO_CFG_WINDOWS").is_ok() {
        // Create a new resource file
        let mut res = winres::WindowsResource::new();
        
        // Set the application icon
        res.set_icon("assets/app_icon_256.ico")
           .set("FileDescription", "Markdown Viewer")
           .set("ProductName", "Markdown Viewer")
           .set("OriginalFilename", "markdown_viewer.exe");
        
        // Compile the resource file
        if let Err(e) = res.compile() {
            // If there's an error, write it to stderr and exit with error code 1
            let _ = writeln!(
                std::io::stderr(),
                "Error setting Windows icon: {}",
                e
            );
            std::process::exit(1);
        }
    }
}
