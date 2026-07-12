import os
import glob
import json
import time
import argparse
from sentence_transformers import SentenceTransformer
import faiss
import numpy as np
from watchdog.observers import Observer
from watchdog.events import FileSystemEventHandler

# Paths configuration
BASE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..', '..')) # Project root
SKILLS_DIR = os.path.join(BASE_DIR, '.agents', 'skills')
INDEX_DIR = os.path.join(os.path.dirname(__file__), 'index')
INDEX_FILE = os.path.join(INDEX_DIR, 'skills.index')
META_FILE = os.path.join(INDEX_DIR, 'skills_meta.json')

os.makedirs(INDEX_DIR, exist_ok=True)

# Lazy loading for the model to speed up CLI
_model = None
def get_model():
    global _model
    if _model is None:
        _model = SentenceTransformer('all-MiniLM-L6-v2')
    return _model

def parse_skill_file(filepath):
    """Parse a markdown skill file and extract text for embedding."""
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
        
        # We index up to the first 2000 characters to capture core metadata & body,
        # as suggested by SKILLWEAVER (Body-aware encoding ns ⊕ ds ⊕ bs[:2000]).
        text_to_index = content[:2000] 
        return {
            'filepath': filepath,
            'filename': os.path.basename(filepath),
            'text': text_to_index
        }
    except Exception as e:
        print(f"Error parsing {filepath}: {e}")
        return None

def build_index():
    """Build the FAISS IndexFlatIP from skill markdown files."""
    print("Building skill FAISS index...")
    md_files = glob.glob(os.path.join(SKILLS_DIR, '**', '*.md'), recursive=True)
    
    metadata = []
    texts = []
    
    for f in md_files:
        # Ignore internal venv or self-evolution engine files to prevent noise
        if 'skillopt-engine' in f or 'venv' in f or 'index' in f: 
            continue 
            
        parsed = parse_skill_file(f)
        if parsed:
            texts.append(parsed['text'])
            metadata.append({'filepath': parsed['filepath'], 'filename': parsed['filename']})
            
    if not texts:
        print("No skills found.")
        return
        
    model = get_model()
    embeddings = model.encode(texts, normalize_embeddings=True)
    
    dimension = embeddings.shape[1]
    index = faiss.IndexFlatIP(dimension) # Inner product search (Cosine similarity since L2 normalized)
    index.add(np.array(embeddings).astype('float32'))
    
    faiss.write_index(index, INDEX_FILE)
    with open(META_FILE, 'w', encoding='utf-8') as f:
        json.dump(metadata, f, ensure_ascii=False, indent=2)
        
    print(f"Index built successfully with {len(metadata)} skills.")

class SkillChangeHandler(FileSystemEventHandler):
    """Watchdog handler to rebuild index on skill updates."""
    def on_modified(self, event):
        if event.src_path.endswith('.md'):
            print(f"Detected change in {event.src_path}. Rebuilding index...")
            build_index()
            
    def on_created(self, event):
        if event.src_path.endswith('.md'):
            print(f"Detected new file {event.src_path}. Rebuilding index...")
            build_index()

def start_daemon():
    """Start the background watchdog daemon."""
    build_index()
    observer = Observer()
    event_handler = SkillChangeHandler()
    observer.schedule(event_handler, SKILLS_DIR, recursive=True)
    observer.start()
    print("Skill Indexer Daemon started. Watching for changes in .agents/skills/...")
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        observer.stop()
    observer.join()

def search_skills(query, k=15):
    """Search for the top-k skills given a query."""
    if not os.path.exists(INDEX_FILE):
        build_index()
        
    index = faiss.read_index(INDEX_FILE)
    with open(META_FILE, 'r', encoding='utf-8') as f:
        metadata = json.load(f)
        
    model = get_model()
    q_emb = model.encode([query], normalize_embeddings=True)
    distances, indices = index.search(np.array(q_emb).astype('float32'), min(k, len(metadata)))
    
    results = []
    for i, idx in enumerate(indices[0]):
        if idx < len(metadata):
            results.append({
                'score': float(distances[0][i]),
                'file': metadata[idx]['filename'],
                'path': metadata[idx]['filepath']
            })
    return results

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="SKILLWEAVER Vector Indexer & Retriever")
    parser.add_argument('--build', action='store_true', help='Force rebuild the FAISS index')
    parser.add_argument('--daemon', action='store_true', help='Start the auto-update watchdog daemon')
    parser.add_argument('--search', type=str, help='Query string to search for relevant skills')
    parser.add_argument('--k', type=int, default=15, help='Number of skills to retrieve (default H=15)')
    
    args = parser.parse_args()
    
    if args.build:
        build_index()
    elif args.search:
        results = search_skills(args.search, args.k)
        print(json.dumps(results, indent=2, ensure_ascii=False))
    elif args.daemon:
        start_daemon()
    else:
        parser.print_help()
