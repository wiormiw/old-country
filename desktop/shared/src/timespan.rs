//! .NET `TimeSpan` in System.Text.Json's format: `[-][d.]hh:mm:ss[.fffffff]`, e.g. `"00:03:35"`
//! or `"1.02:03:04.5000000"`. Kept as fractional seconds, which is what the UI and player need.

use serde::{Deserialize, Deserializer, Serializer, de::Error};

/// Parses a TimeSpan string into seconds.
pub fn parse(text: &str) -> Option<f64> {
    let (negative, text) = match text.strip_prefix('-') {
        Some(rest) => (true, rest),
        None => (false, text),
    };
    let mut parts = text.split(':');
    let (first, minutes, seconds) = (parts.next()?, parts.next()?, parts.next()?);
    if parts.next().is_some() {
        return None;
    }
    let (days, hours) = match first.split_once('.') {
        Some((days, hours)) => (days.parse::<f64>().ok()?, hours.parse::<f64>().ok()?),
        None => (0.0, first.parse::<f64>().ok()?),
    };
    let total = days * 86_400.0 + hours * 3_600.0 + minutes.parse::<f64>().ok()? * 60.0 + seconds.parse::<f64>().ok()?;
    Some(if negative { -total } else { total })
}

/// Formats seconds as a TimeSpan string (whole seconds).
pub fn format(seconds: f64) -> String {
    let total = seconds.round() as i64;
    let sign = if total < 0 { "-" } else { "" };
    let total = total.abs();
    let (days, rest) = (total / 86_400, total % 86_400);
    let (hours, minutes, secs) = (rest / 3_600, rest % 3_600 / 60, rest % 60);
    if days > 0 {
        format!("{sign}{days}.{hours:02}:{minutes:02}:{secs:02}")
    } else {
        format!("{sign}{hours:02}:{minutes:02}:{secs:02}")
    }
}

/// Human display: `3:35`, `1:02:03`.
pub fn display(seconds: f64) -> String {
    let total = seconds.max(0.0).round() as u64;
    let (hours, minutes, secs) = (total / 3_600, total % 3_600 / 60, total % 60);
    if hours > 0 { format!("{hours}:{minutes:02}:{secs:02}") } else { format!("{minutes}:{secs:02}") }
}

/// `#[serde(with = "timespan::seconds")]` for `f64` fields.
pub mod seconds {
    use super::*;

    pub fn serialize<S: Serializer>(value: &f64, serializer: S) -> Result<S::Ok, S::Error> {
        serializer.serialize_str(&format(*value))
    }

    pub fn deserialize<'de, D: Deserializer<'de>>(deserializer: D) -> Result<f64, D::Error> {
        let text = String::deserialize(deserializer)?;
        parse(&text).ok_or_else(|| D::Error::custom(format!("not a TimeSpan: {text:?}")))
    }
}

/// `#[serde(default, with = "timespan::optional")]` for `Option<f64>` fields.
pub mod optional {
    use super::*;

    pub fn serialize<S: Serializer>(value: &Option<f64>, serializer: S) -> Result<S::Ok, S::Error> {
        match value {
            Some(seconds) => serializer.serialize_some(&format(*seconds)),
            None => serializer.serialize_none(),
        }
    }

    pub fn deserialize<'de, D: Deserializer<'de>>(deserializer: D) -> Result<Option<f64>, D::Error> {
        match Option::<String>::deserialize(deserializer)? {
            Some(text) => parse(&text).map(Some).ok_or_else(|| D::Error::custom(format!("not a TimeSpan: {text:?}"))),
            None => Ok(None),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_dotnet_timespans() {
        assert_eq!(parse("00:03:35"), Some(215.0));
        assert_eq!(parse("1.02:03:04.5000000"), Some(93_784.5));
        assert_eq!(parse("-00:00:01"), Some(-1.0));
        assert_eq!(parse("3:35"), None);
        assert_eq!(parse("nonsense"), None);
    }

    #[test]
    fn formats_round_trip() {
        assert_eq!(format(215.0), "00:03:35");
        assert_eq!(format(93_784.0), "1.02:03:04");
        assert_eq!(parse(&format(4_000.0)), Some(4_000.0));
    }

    #[test]
    fn displays_for_people() {
        assert_eq!(display(215.4), "3:35");
        assert_eq!(display(3_723.0), "1:02:03");
        assert_eq!(display(-5.0), "0:00");
    }
}
