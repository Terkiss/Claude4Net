#!/usr/bin/env python3
import os
import glob
import json
import time
import argparse
import hashlib
import re

# Paths configuration
BASE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..', '..')) # Project root
SKILLS_DIR = os.path.join(BASE_DIR, '.agents', 'skills')
INDEX_DIR = os.path.join(os.path.dirname(__file__), 'index')
INDEX_FILE = os.path.join(INDEX_DIR, 'skills.index')
META_FILE = os.path.join(INDEX_DIR, 'skills_meta.json')

os.makedirs(INDEX_DIR, exist_ok=True)

# Lazy import check
HAS_DEPS = True
try:
    from sentence_transformers import SentenceTransformer
    import faiss
    import numpy as np
except ImportError:
    HAS_DEPS = False

_model = None
def get_model():
    global _model
    if not HAS_DEPS:
        return None
    if _model is None:
        try:
            _model = SentenceTransformer('all-MiniLM-L6-v2')
        except Exception as e:
            print(f"[indexer] Error loading SentenceTransformer: {e}. Falling back to lexical.")
            return None
    return _model

def get_content_hash(filepath):
    try:
        with open(filepath, 'rb') as f:
            return hashlib.md5(f.read()).hexdigest()
    except Exception:
        return ""

def parse_skill_file(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
        
        text_to_index = content[:2000] 
        rel_path = os.path.relpath(filepath, BASE_DIR).replace('\\', '/')
        
        name = ""
        description = ""
        if content.startswith("---"):
            parts = content.split("---", 2)
            if len(parts) >= 3:
                for line in parts[1].splitlines():
                    if ":" in line:
                        k, v = line.split(":", 1)
                        if k.strip() == "name":
                            name = v.strip().strip('"').strip("'")
                        elif k.strip() == "description":
                            description = v.strip().strip('"').strip("'")
                            
        return {
            'filepath': rel_path,
            'filename': os.path.basename(filepath),
            'name': name or os.path.basename(os.path.dirname(filepath)),
            'description': description,
            'text': text_to_index,
            'content_hash': get_content_hash(filepath)
        }
    except Exception as e:
        print(f"[indexer] Error parsing {filepath}: {e}")
        return None

def build_index():
    print("Building top-level skill index...")
    md_files = glob.glob(os.path.join(SKILLS_DIR, '*', 'SKILL.md'))
    
    metadata = []
    texts = []
    
    for f in md_files:
        parsed = parse_skill_file(f)
        if parsed:
            texts.append(parsed['text'])
            metadata.append({
                'filepath': parsed['filepath'],
                'filename': parsed['filename'],
                'name': parsed['name'],
                'description': parsed['description'],
                'content_hash': parsed['content_hash']
            })
            
    if not metadata:
        print("No skills found to index.")
        return
        
    model = get_model()
    if HAS_DEPS and model is not None:
        try:
            embeddings = model.encode(texts, normalize_embeddings=True)
            dimension = embeddings.shape[1]
            index = faiss.IndexFlatIP(dimension)
            index.add(np.array(embeddings).astype('float32'))
            faiss.write_index(index, INDEX_FILE)
            print("[indexer] FAISS index built successfully.")
        except Exception as e:
            print(f"[indexer] Failed to build FAISS index: {e}. Storing metadata for lexical search only.")
    else:
        print("[indexer] Missing dependencies (FAISS/SentenceTransformer). Storing metadata for lexical search.")
        
    with open(META_FILE, 'w', encoding='utf-8') as f:
        json.dump(metadata, f, ensure_ascii=False, indent=2)
        
    print(f"Index metadata updated with {len(metadata)} skills.")

def lexical_search_fallback(query, metadata, k=15):
    query_words = set(re.findall(r'\w+', query.lower()))
    if not query_words:
        return [{
            'score': 1.0,
            'name': meta['name'],
            'file': meta['filename'],
            'path': meta['filepath']
        } for meta in metadata[:k]]
        
    results = []
    for meta in metadata:
        full_path = os.path.join(BASE_DIR, meta['filepath'])
        try:
            with open(full_path, 'r', encoding='utf-8') as f:
                content = f.read().lower()
        except Exception:
            content = (meta['name'] + " " + meta['description']).lower()
            
        score = 0
        for word in query_words:
            if word in meta['name'].lower():
                score += 3
            if word in meta['description'].lower():
                score += 2
            if word in content:
                score += 1
                
        results.append({
            'score': float(score),
            'name': meta['name'],
            'file': meta['filename'],
            'path': meta['filepath']
        })
        
    results.sort(key=lambda x: x['score'], reverse=True)
    return results[:k]

def search_skills(query, k=15):
    if not os.path.exists(META_FILE):
        build_index()
        
    with open(META_FILE, 'r', encoding='utf-8') as f:
        metadata = json.load(f)
        
    if not metadata:
        return []
        
    model = get_model()
    if HAS_DEPS and model is not None and os.path.exists(INDEX_FILE):
        try:
            index = faiss.read_index(INDEX_FILE)
            q_emb = model.encode([query], normalize_embeddings=True)
            distances, indices = index.search(np.array(q_emb).astype('float32'), min(k, len(metadata)))
            
            results = []
            for i, idx in enumerate(indices[0]):
                if idx < len(metadata):
                    meta = metadata[idx]
                    results.append({
                        'score': float(distances[0][i]),
                        'name': meta['name'],
                        'file': meta['filename'],
                        'path': meta['filepath']
                    })
            return results
        except Exception as e:
            print(f"[indexer] FAISS search failed: {e}. Falling back to lexical search.", file=sys.stderr)
            
    return lexical_search_fallback(query, metadata, k)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="SKILLWEAVER Vector Indexer & Retriever")
    parser.add_argument('--build', action='store_true', help='Force rebuild the index')
    parser.add_argument('--check', action='store_true', help='Check and return current index status')
    parser.add_argument('--search', type=str, help='Query string to search for relevant skills')
    parser.add_argument('--k', type=int, default=15, help='Number of skills to retrieve')
    
    args = parser.parse_args()
    
    if args.build:
        build_index()
    elif args.check:
        if os.path.exists(META_FILE):
            with open(META_FILE, 'r', encoding='utf-8') as f:
                meta = json.load(f)
            print(json.dumps({
                "status": "OK",
                "indexed_skills": len(meta),
                "has_faiss": HAS_DEPS
            }))
        else:
            print(json.dumps({
                "status": "MISSING",
                "indexed_skills": 0,
                "has_faiss": HAS_DEPS
            }))
    elif args.search:
        results = search_skills(args.search, args.k)
        print(json.dumps(results, indent=2, ensure_ascii=False))
    else:
        parser.print_help()
