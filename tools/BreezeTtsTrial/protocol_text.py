"""Exact protocol fields and word counting; never interprets dialogue intent."""
import string

def valid_identifier(value):
    return (isinstance(value, str) and 1 <= len(value) <= 128
            and value[0] in string.ascii_letters + string.digits
            and all(c in string.ascii_letters + string.digits + '._-' for c in value))

def valid_source(value):
    return (isinstance(value, str) and 1 <= len(value) <= 96
            and all(c in string.ascii_letters + string.digits + '._:-' for c in value))

def valid_sha256(value, lowercase=False):
    alphabet='0123456789abcdef' if lowercase else string.hexdigits
    return isinstance(value,str) and len(value)==64 and all(c in alphabet for c in value)

def word_count(text):
    count=0; inside=False
    for i,c in enumerate(text):
        if c.isalnum() or c=='_':
            if not inside: count+=1
            inside=True
        elif c in "'’" and inside and i+1<len(text) and (text[i+1].isalnum() or text[i+1]=='_'):
            continue
        else: inside=False
    return count
