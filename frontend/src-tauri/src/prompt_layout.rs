use std::sync::{Arc, atomic::{AtomicU64, Ordering}};
use std::time::{Duration, Instant};
use windows_sys::Win32::UI::WindowsAndMessaging::{SetWindowPos, SWP_NOACTIVATE, SWP_NOZORDER};

pub const COLLAPSED_HEIGHT: f64 = 340.0;
const CHOICE_HEIGHT: f64 = 44.0;

#[derive(Default)]
pub struct Motion { pub generation: Arc<AtomicU64> }

fn height_at(from: i32, to: i32, progress: f64, expanding: bool) -> i32 {
    let t = progress.clamp(0.0, 1.0);
    let eased = if expanding { 1.0 - (1.0 - t).powi(3) } else { t.powi(3) };
    (from as f64 + (to - from) as f64 * eased).round() as i32
}

fn target_height(scale: f64, expanded: bool, compact: bool, chrome: i32, available: i32) -> i32 {
    (((COLLAPSED_HEIGHT - if compact { 40.0 } else { 0.0 } + if expanded { CHOICE_HEIGHT } else { 0.0 }) * scale).round() as i32 + chrome)
        .clamp(1, available.max(1))
}

pub async fn resize(window: tauri::WebviewWindow, generation: Arc<AtomicU64>, expanded: bool, reduced_motion: bool, compact: bool) -> Result<(), String> {
    if window.label() != "prompt" { return Err("此窗口不能调整设备提示大小".into()); }
    let revision = generation.fetch_add(1, Ordering::SeqCst) + 1;
    let position = window.outer_position().map_err(|e| e.to_string())?;
    let outer = window.outer_size().map_err(|e| e.to_string())?;
    let inner = window.inner_size().map_err(|e| e.to_string())?;
    let bottom = position.y + outer.height as i32;
    let monitor = window.current_monitor().map_err(|e| e.to_string())?;
    let available = monitor.map(|m| bottom - m.work_area().position.y).unwrap_or(i32::MAX);
    let scale = window.scale_factor().map_err(|e| e.to_string())?;
    let target = target_height(scale, expanded, compact, outer.height as i32 - inner.height as i32, available);
    let growing = target > outer.height as i32;
    let started = Instant::now();
    let duration = if growing { 250.0 } else { 200.0 };
    loop {
        if generation.load(Ordering::SeqCst) != revision { return Ok(()); }
        let t = if reduced_motion { 1.0 } else { (started.elapsed().as_secs_f64() * 1000.0 / duration).min(1.0) };
        let height = height_at(outer.height as i32, target, t, growing);
        let current = window.clone();
        let guard = generation.clone();
        let (done, finished) = tokio::sync::oneshot::channel();
        window.run_on_main_thread(move || {
            let result = if guard.load(Ordering::SeqCst) != revision { Ok(()) } else {
                current.hwnd().map_err(|e| e.to_string()).and_then(|handle| {
                    // One native update keeps the bottom edge fixed, without activation or a position/size flash.
                    let ok = unsafe { SetWindowPos(handle.0 as _, std::ptr::null_mut(), position.x, bottom - height,
                        outer.width as i32, height, SWP_NOACTIVATE | SWP_NOZORDER) };
                    if ok == 0 { Err(std::io::Error::last_os_error().to_string()) } else { Ok(()) }
                })
            };
            let _ = done.send(result);
        }).map_err(|e| e.to_string())?;
        finished.await.map_err(|e| e.to_string())??;
        if t >= 1.0 { return Ok(()); }
        tokio::time::sleep(Duration::from_millis(16)).await;
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn upward_resize_is_bounded_and_reverses_from_current_height() {
        for scale in [1.0, 1.25, 1.5, 2.0] {
            let closed = target_height(scale, false, false, 0, 2000);
            let open = target_height(scale, true, false, 0, 2000);
            let mid = height_at(closed, open, 0.5, true);
            assert!(closed < mid && mid < open);
            assert_eq!(height_at(mid, closed, 0.0, false), mid);
            assert_eq!(height_at(mid, closed, 1.0, false), closed);
        }
        assert_eq!(target_height(2.0, true, false, 0, 500), 500);
        assert_eq!(target_height(1.0, false, false, 30, 1000), 370);
        assert_eq!(target_height(1.0, false, true, 0, 1000), 300);
        assert_eq!(target_height(1.5, true, true, 0, 1000), 516);
    }
}
