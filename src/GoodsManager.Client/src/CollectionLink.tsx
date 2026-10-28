import type {ComponentProps} from 'react';
import {useCollectionApi} from './CollectionContext';
import {collectionUrl} from './navigation';
export function CollectionLink({href,...props}:ComponentProps<'a'>){const api=useCollectionApi();return <a {...props} href={href?collectionUrl(href,api.collectionId):href}/>;}
