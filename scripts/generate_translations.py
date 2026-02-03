import requests
import csv
import json
import opencc

# xivapi datamining CSV sources
XIVAPI_BASE_URL = "https://raw.githubusercontent.com/xivapi/ffxiv-datamining/refs/heads/master/csv"
LANGUAGES = {
    "en": f"{XIVAPI_BASE_URL}/en/Action.csv",
    "ja": f"{XIVAPI_BASE_URL}/ja/Action.csv",
    "de": f"{XIVAPI_BASE_URL}/de/Action.csv",
    "fr": f"{XIVAPI_BASE_URL}/fr/Action.csv",
}

# Chinese server data source (separate repo)
CN_ACTION_CSV_URL = "https://raw.githubusercontent.com/thewakingsands/ffxiv-datamining-cn/refs/heads/master/Action.csv"

# Output filenames
OUTPUT_FILES = {
    "en": "actions_en.json",
    "ja": "actions_ja.json",
    "de": "actions_de.json",
    "fr": "actions_fr.json",
    "zhtw": "actions_zhtw.json",
}


def download_csv(url):
    """Download CSV from URL and return parsed rows."""
    print(f"Downloading from {url}...")
    response = requests.get(url)
    response.raise_for_status()
    decoded_content = response.content.decode('utf-8')
    return csv.reader(decoded_content.splitlines())


def parse_action_csv(csv_reader):
    """Parse action CSV and return {id: name} dictionary."""
    action_map = {}
    for row in csv_reader:
        if len(row) < 2 or not row[0].isdigit():
            continue
        action_id = row[0]
        action_name = row[1]
        if action_name:
            action_map[action_id] = action_name
    return action_map


def save_json(data, filename):
    """Save dictionary to JSON file."""
    with open(filename, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, separators=(',', ':'))
    print(f"Generated {filename} with {len(data)} entries")


def main():
    # Process xivapi languages (EN, JA, DE, FR)
    for lang, url in LANGUAGES.items():
        try:
            csv_reader = download_csv(url)
            action_map = parse_action_csv(csv_reader)
            save_json(action_map, OUTPUT_FILES[lang])
        except Exception as e:
            print(f"Failed to process {lang}: {e}")
            exit(1)

    # Process Chinese (Simplified -> Traditional)
    try:
        csv_reader = download_csv(CN_ACTION_CSV_URL)
        action_map = parse_action_csv(csv_reader)

        # Convert Simplified Chinese to Traditional Chinese (Taiwan)
        converter = opencc.OpenCC('s2twp.json')
        action_map_tc = {k: converter.convert(v) for k, v in action_map.items()}

        save_json(action_map_tc, OUTPUT_FILES["zhtw"])
    except Exception as e:
        print(f"Failed to process Chinese: {e}")
        exit(1)

    print("All translations generated successfully!")


if __name__ == "__main__":
    main()
