import {createContext,useContext} from 'react';
import {api,type ApiClient} from './api';
export const CollectionApiContext=createContext<ApiClient>(api);
export const useCollectionApi=()=>useContext(CollectionApiContext);
